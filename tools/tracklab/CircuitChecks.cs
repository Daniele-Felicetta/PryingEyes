using System;
using System.Collections.Generic;
using System.IO;
using PryingEyes.Pista;

static partial class Program
{
    const int CircuitSeeds = 40;

    static Circuit[] labCircuits;

    /// <summary>The circuits the checks (and the race checks) use: generated once.</summary>
    static Circuit[] LabCircuits()
    {
        if (labCircuits != null) return labCircuits;
        labCircuits = new Circuit[CircuitSeeds];
        for (int i = 0; i < CircuitSeeds; i++) labCircuits[i] = CircuitGen.Generate(1 + i * 7919 + seedArg);
        return labCircuits;
    }

    static int CircularDiff(int a, int b, int n) { int d = Math.Abs(a - b); return Math.Min(d, n - d); }

    static void CircuitChecks()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int fallbacks = 0, maxAttempts = 0; double sumAttempts = 0;
        var circuits = new Circuit[CircuitSeeds];
        for (int i = 0; i < CircuitSeeds; i++)
        {
            int att;
            circuits[i] = CircuitGen.Generate(1 + i * 7919 + seedArg, out att);
            if (att > CircuitGen.MaxAttempts) fallbacks++;
            maxAttempts = Math.Max(maxAttempts, att); sumAttempts += att;
        }
        labCircuits = circuits;
        Check("CIRCUIT_SEEDS_MAKE_REAL_CIRCUITS", fallbacks == 0,
            CircuitSeeds + " seeds in " + sw.ElapsedMilliseconds + " ms, " + fallbacks + " fell back to the plain circuit, attempts mean " + (sumAttempts / CircuitSeeds).ToString("0.0") + " max " + maxAttempts,
            "every seed gives a drawn circuit");

        double sumLen = 0; double minLen = 1e9, maxLen = 0, worstClose = 0, worstHead = 0, maxSlope = 0, minRun = 1e9, minRadius = 1e9, maxStraight = 0, worstWall = 1e9, worstGap = 1e9, worstGrid = 0;
        double worstVertical = 0;
        int cw = 0, widthBad = 0;
        string worstWallSeed = "", worstGapSeed = "";
        for (int ci = 0; ci < circuits.Length; ci++)
        {
            var c = circuits[ci];
            minLen = Math.Min(minLen, c.length); maxLen = Math.Max(maxLen, c.length); sumLen += c.length;
            if (c.clockwise) cw++;
            // closure: the sample after the last must land on the first
            int n = c.n;
            double ex = c.px[n - 1] + c.tx[n - 1] * c.spacing - c.px[0], ez = c.pz[n - 1] + c.tz[n - 1] * c.spacing - c.pz[0];
            worstClose = Math.Max(worstClose, Math.Sqrt(ex * ex + ez * ez));
            double h0 = Math.Atan2(c.tx[0], c.tz[0]), h1 = Math.Atan2(c.tx[n - 1], c.tz[n - 1]);
            worstHead = Math.Max(worstHead, Math.Abs(PM.WrapPi((float)(h0 - h1))) / c.spacing * 100);
            // slope, run-off, tightest corner, longest straight
            double run = 0;
            for (int i = 0; i < n; i++)
            {
                int j = i + 1 == n ? 0 : i + 1;
                maxSlope = Math.Max(maxSlope, Math.Abs(c.py[j] - c.py[i]) / c.spacing);
                minRun = Math.Min(minRun, Math.Min(c.run[0][i], c.run[1][i]));
                if (c.run[0][i] < c.kerb[0][i] + c.apron[0][i] + c.trap[0][i] || c.run[1][i] < c.kerb[1][i] + c.apron[1][i] + c.trap[1][i]) widthBad++;
                double k = Math.Abs(c.curv[i]);
                if (k > 1e-6) minRadius = Math.Min(minRadius, 1.0 / k);
                if (k < 1e-5) { run += c.spacing; maxStraight = Math.Max(maxStraight, run); } else run = 0;
                worstVertical = Math.Max(worstVertical, Math.Abs(c.vcurv[i]));
            }
            // no two parts of the road come closer than the two asphalts plus 8 m, and no barrier stands on another road
            for (int i = 0; i < n; i += 3)
                for (int j = 0; j < n; j += 2)
                {
                    int dl = CircularDiff(i, j, n);
                    if (dl * c.spacing < 90f) continue;
                    double dx = c.px[j] - c.px[i], dz = c.pz[j] - c.pz[i];
                    double d = Math.Sqrt(dx * dx + dz * dz) - c.halfW[i] - c.halfW[j];
                    if (d < worstGap) { worstGap = d; worstGapSeed = "seed index " + ci; }
                }
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < n; i += 3)
                {
                    float off = (c.halfW[i] + c.run[side][i]) * (side == 1 ? 1f : -1f);
                    float bx = c.px[i] + c.nx[i] * off, bz = c.pz[i] + c.nz[i] * off;
                    for (int j = 0; j < n; j += 2)
                    {
                        if (CircularDiff(i, j, n) * c.spacing < 90f) continue;
                        double dx = c.px[j] - bx, dz = c.pz[j] - bz;
                        double d = Math.Sqrt(dx * dx + dz * dz) - c.halfW[j];
                        if (d < worstWall) { worstWall = d; worstWallSeed = "seed index " + ci; }
                    }
                }
            // the grid: twenty slots (fifteen rivals, the player and three friends, one spare) on a straight, flat, on asphalt
            for (int k = 0; k < 20; k++)
            {
                float gx, gz, gy, gh;
                c.GridSlot(k, out gx, out gz, out gy, out gh);
                int hint = -1;
                GroundSample gs;
                c.Sample(gx, gz, ref hint, out gs);
                Proj pr;
                c.Project(gx, gz, ref hint, out pr);
                double bad = (gs.surface == SurfaceKind.Asphalt ? 0 : 100) + Math.Abs(Math.Abs(pr.d) - 1.8) + Math.Abs(c.curv[hint]) * 1000 + Math.Abs(gs.y - gy) * 10;
                worstGrid = Math.Max(worstGrid, bad);
            }
        }
        Check("CIRCUIT_LENGTHS", minLen >= 2400 && maxLen <= 6000, "lap lengths " + minLen.ToString("0") + " to " + maxLen.ToString("0") + " m (mean " + (sumLen / CircuitSeeds).ToString("0") + "), " + cw + " of " + CircuitSeeds + " clockwise",
            "between 2.4 and 6 km, both directions");
        Check("CIRCUIT_CLOSES", worstClose < 0.05 && worstHead < 1.0, "closure error " + worstClose.ToString("0.0000") + " m, heading step " + worstHead.ToString("0.000") + " 1/100 rad", "under 5 cm and no jump in heading");
        Check("CIRCUIT_SLOPE_AND_CREST", maxSlope <= 0.085, "steepest " + (maxSlope * 100).ToString("0.0") + " %, strongest vertical curvature " + worstVertical.ToString("0.00000") + " 1/m", "at most 8.5 % grade");
        Check("CIRCUIT_RUNOFF_ROOM", minRun >= 1.2 && widthBad == 0, "narrowest run-off " + minRun.ToString("0.0") + " m, samples where the paving does not fit " + widthBad, "at least 1.2 m of grass before the barrier everywhere");
        Check("CIRCUIT_ROADS_KEEP_APART", worstGap >= 6.0, "closest two roads, edge to edge " + worstGap.ToString("0.0") + " m (" + worstGapSeed + ")", "6 m or more");
        Check("CIRCUIT_NO_WALL_ON_A_ROAD", worstWall >= 0.8, "closest barrier to another road's asphalt " + worstWall.ToString("0.0") + " m (" + worstWallSeed + ")", "never on the asphalt");
        Check("CIRCUIT_GRID_IS_ON_A_STRAIGHT", worstGrid < 1.0, "worst grid slot deviation " + worstGrid.ToString("0.00"), "twenty slots on flat straight asphalt behind the line");
        Check("CIRCUIT_HAS_CHARACTER", minRadius < 40 && maxStraight > 300, "tightest corner radius " + minRadius.ToString("0") + " m, longest straight " + maxStraight.ToString("0") + " m", "a slow corner and a long straight somewhere");

        // determinism and variety
        var again = CircuitGen.Generate(1 + 3 * 7919 + seedArg);
        var first = circuits[3];
        bool same = again.n == first.n && again.length == first.length;
        for (int i = 0; same && i < first.n; i++) same = again.px[i] == first.px[i] && again.pz[i] == first.pz[i] && again.py[i] == first.py[i] && again.run[0][i] == first.run[0][i];
        Check("CIRCUIT_SAME_SEED_SAME_CIRCUIT", same && again.name == first.name, "name '" + first.name + "', " + first.n + " samples, identical on a second run: " + same, "the host only sends a number");
        var lens = new HashSet<int>();
        foreach (var c in circuits) lens.Add((int)(c.length / 5));
        Check("CIRCUIT_SEEDS_DIFFER", lens.Count >= CircuitSeeds - 2, lens.Count + " different lap lengths among " + CircuitSeeds + " seeds", "mostly different circuits");

        // the projection finds the same place as a brute-force search, with and without a hint, and along a ride
        int mismatch = 0, total = 0; double worstD = 0; string firstBad = "";
        var rr = new Rng(77);
        for (int ci = 0; ci < circuits.Length; ci += 4)
        {
            var c = circuits[ci];
            for (int q = 0; q < 200; q++)
            {
                int k = rr.Int(c.n);
                float off = rr.Range(-24f, 24f), al = rr.Range(-6f, 6f);
                float qx = c.px[k] + c.nx[k] * off + c.tx[k] * al, qz = c.pz[k] + c.nz[k] * off + c.tz[k] * al;
                int hint = -1;
                Proj p;
                c.Project(qx, qz, ref hint, out p);
                int bi; double bd, bt;
                Brute(c, qx, qz, out bi, out bt, out bd);
                total++;
                if (CircularDiff(p.i, bi, c.n) > 1 || Math.Abs(Math.Abs(p.d) - bd) > 0.1) { mismatch++; if (firstBad == "") firstBad = " first: circuit " + ci + " sample " + k + " off " + off.ToString("0.0") + " got i=" + p.i + " d=" + p.d.ToString("0.000") + " brute i=" + bi + " d=" + bd.ToString("0.000"); }
                worstD = Math.Max(worstD, Math.Abs(Math.Abs(p.d) - bd));
            }
            // a ride along the lap, three metres right of the centreline, keeping the hint
            int h2 = 0;
            for (int i = 0; i < c.n * 3; i += 1)
            {
                int k = i % c.n;
                float qx = c.px[k] + c.nx[k] * 3f, qz = c.pz[k] + c.nz[k] * 3f;
                Proj p;
                c.Project(qx, qz, ref h2, out p);
                total++;
                if (CircularDiff(p.i, k, c.n) > 2 || Math.Abs(p.d - 3f) > 0.5f) { mismatch++; if (firstBad == "") firstBad = " first (ride): circuit " + ci + " sample " + k + " got i=" + p.i + " d=" + p.d.ToString("0.000"); }
            }
        }
        Check("CIRCUIT_PROJECTION_MATCHES_BRUTE_FORCE", mismatch == 0, total + " queries, " + mismatch + " mismatches, worst distance error " + worstD.ToString("0.0000") + " m" + firstBad, "the fast search never loses the road");

        // the ground: surfaces from the middle outward and the barrier at the end
        {
            var c = circuits[5];
            int k = c.n / 3;
            int hint = k;
            var seen = new List<SurfaceKind>();
            float limR = c.halfW[k] + c.run[1][k];
            for (float off = 0f; off < limR - 0.05f; off += 0.25f)
            {
                GroundSample g;
                c.Sample(c.px[k] + c.nx[k] * off, c.pz[k] + c.nz[k] * off, ref hint, out g);
                if (seen.Count == 0 || seen[seen.Count - 1] != g.surface) seen.Add(g.surface);
            }
            float bnx, bnz, depth;
            bool inside = c.Barrier(c.px[k] + c.nx[k] * (limR - 1f), c.pz[k] + c.nz[k] * (limR - 1f), 0.36f, ref hint, out bnx, out bnz, out depth);
            bool hit = c.Barrier(c.px[k] + c.nx[k] * (limR + 0.2f), c.pz[k] + c.nz[k] * (limR + 0.2f), 0.36f, ref hint, out bnx, out bnz, out depth);
            bool pushesBack = (bnx * c.nx[k] + bnz * c.nz[k]) < -0.9f && depth > 0.3f;
            Check("CIRCUIT_GROUND_BANDS_AND_BARRIER", seen.Count >= 2 && seen[0] == SurfaceKind.Asphalt && !inside && hit && pushesBack,
                "bands from the middle out: " + string.Join(" > ", seen) + "; wall 1 m before: " + inside + ", past it: " + hit + " pushing back with depth " + depth.ToString("0.00"), "asphalt first, then the run-off, a wall that pushes the bike back");
        }

        if (picturesDir != null)
            for (int i = 0; i < 6; i++) DrawCircuit(circuits[i], Path.Combine(picturesDir, "circuit_" + i + ".png"));
    }

    static void Brute(Circuit c, float qx, float qz, out int best, out double bestT, out double dist)
    {
        best = -1; bestT = 0; double bd = double.MaxValue;
        for (int i = 0; i < c.n; i++)
        {
            int j = i + 1 == c.n ? 0 : i + 1;
            double ex = c.px[j] - c.px[i], ez = c.pz[j] - c.pz[i], wx = qx - c.px[i], wz = qz - c.pz[i];
            double t = (wx * ex + wz * ez) / (ex * ex + ez * ez);
            t = Math.Max(0, Math.Min(1, t));
            double cx = wx - ex * t, cz = wz - ez * t, d2 = cx * cx + cz * cz;
            if (d2 < bd) { bd = d2; best = i; bestT = t; }
        }
        dist = Math.Sqrt(bd);
    }

    /// <summary>The circuit seen from above: road, kerbs, run-off, gravel, grass, the barrier, the start line and the hills as a strip underneath.</summary>
    static void DrawCircuit(Circuit c, string path)
    {
        const int W = 1400, H = 1040, Strip = 110;
        var pic = new Picture(W, H, 34, 46, 30);
        float mw = c.maxX - c.minX, mh = c.maxZ - c.minZ;
        float sc = Math.Min((W - 80f) / mw, (H - Strip - 80f) / mh);
        float ox = (W - mw * sc) * 0.5f - c.minX * sc, oy = (H - Strip - mh * sc) * 0.5f;
        Func<float, float> X = wx => ox + wx * sc;
        Func<float, float> Y = wz => (H - Strip) - (oy + (wz - c.minZ) * sc);
        for (int i = 0; i < c.n; i++)
        {
            for (int side = 0; side < 2; side++)
            {
                float sg = side == 1 ? 1f : -1f;
                float hw = c.halfW[i], kw = c.kerb[side][i], aw = c.apron[side][i], tw = c.trap[side][i], rn = c.run[side][i];
                for (float d = 0f; d <= hw + rn; d += 0.5f / Math.Max(sc, 0.2f) * 0.8f)
                {
                    byte r, g, b;
                    if (d <= hw) { r = 92; g = 92; b = 98; }
                    else if (d <= hw + kw) { bool red = ((i / 3) & 1) == 0; r = (byte)(red ? 200 : 235); g = (byte)(red ? 40 : 235); b = (byte)(red ? 40 : 235); }
                    else if (d <= hw + kw + aw) { r = 128; g = 128; b = 134; }
                    else if (d <= hw + kw + aw + tw) { r = 206; g = 182; b = 108; }
                    else if (d <= hw + rn - 0.5f) { r = 56; g = 100; b = 44; }
                    else { r = 240; g = 240; b = 240; }
                    pic.Disc(X(c.px[i] + c.nx[i] * d * sg), Y(c.pz[i] + c.nz[i] * d * sg), Math.Max(0.8f, sc * 1.1f), r, g, b);
                }
            }
        }
        // start line and grid
        float lx0 = c.px[0] - c.nx[0] * c.halfW[0], lz0 = c.pz[0] - c.nz[0] * c.halfW[0], lx1 = c.px[0] + c.nx[0] * c.halfW[0], lz1 = c.pz[0] + c.nz[0] * c.halfW[0];
        pic.Line(X(lx0), Y(lz0), X(lx1), Y(lz1), 255, 255, 255, 3f);
        for (int k = 0; k < 12; k++) { float gx, gz, gy, gh; c.GridSlot(k, out gx, out gz, out gy, out gh); pic.Disc(X(gx), Y(gz), 2f, 255, 210, 40); }
        // the direction: an arrow every 400 m
        for (float s = 200f; s < c.length; s += 400f)
        {
            float ax, az, ay, ah; c.At(s, out ax, out az, out ay, out ah);
            float tx = (float)Math.Sin(ah), tz = (float)Math.Cos(ah);
            float x0 = X(ax), y0 = Y(az), x1 = X(ax + tx * 30f), y1 = Y(az + tz * 30f);
            pic.Line(x0, y0, x1, y1, 255, 120, 30, 2f);
            pic.Disc(x1, y1, 4f, 255, 120, 30);
        }
        // the hills
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < c.n; i++) { lo = Math.Min(lo, c.py[i]); hi = Math.Max(hi, c.py[i]); }
        float span = Math.Max(hi - lo, 1f);
        for (int px = 0; px < W - 60; px++)
        {
            int i = (int)((px / (float)(W - 60)) * c.n) % c.n;
            float yy = H - 14 - (c.py[i] - lo) / span * (Strip - 30);
            pic.Line(px + 30, H - 14, px + 30, yy, 70, 90, 140);
            pic.Set(px + 30, (int)yy, 240, 240, 255);
        }
        pic.Save(path);
    }
}
