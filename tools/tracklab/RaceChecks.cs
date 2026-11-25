using System;
using System.Collections.Generic;
using System.IO;
using PryingEyes.Pista;
using Production.Racing;

static partial class Program
{
    static void RaceChecks()
    {
        var circuits = LabCircuits();
        var spec = Bikes.Superbike();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int LineCircuits = 10;
        double sumOpt = 0, sumCentre = 0, worstOver = 0, worstOff = 0, worstBrake = 0, worstAccel = 0, minAvg = 1e9, maxAvg = 0, worstKappa = 0, worstLimitOver = 0;
        double buildMs = 0;
        var lines = new RacingLine[LineCircuits];
        for (int i = 0; i < LineCircuits; i++)
        {
            var c = circuits[i];
            var t0 = sw.ElapsedMilliseconds;
            var line = new RacingLine(c, spec);
            buildMs += sw.ElapsedMilliseconds - t0;
            lines[i] = line;
            var centre = new RacingLine(c, spec, centreOnly: true);
            sumOpt += line.lapTime; sumCentre += centre.lapTime;
            double avg = c.length / line.lapTime * 3.6;
            minAvg = Math.Min(minAvg, avg); maxAvg = Math.Max(maxAvg, avg);
            var curves = line.curves;
            for (int k = 0; k < c.n; k++)
            {
                int nx = (k + 1) % c.n;
                worstOff = Math.Max(worstOff, Math.Abs(line.off[k]) - (c.halfW[k] - 1.2));
                worstLimitOver = Math.Max(worstLimitOver, line.vplan[k] - line.vlim[k]);
                float dx = line.x[nx] - line.x[k], dz = line.z[nx] - line.z[k];
                float ds = (float)Math.Sqrt(dx * dx + dz * dz);
                float a = (line.vplan[nx] * line.vplan[nx] - line.vplan[k] * line.vplan[k]) / (2f * ds);
                if (a > 0) worstAccel = Math.Max(worstAccel, a - (curves.AccelAt(line.vplan[k]) + RacingMath.G * 0.1f));
                else worstBrake = Math.Max(worstBrake, -a - (curves.BrakeAt(line.vplan[nx]) + RacingMath.G * 0.1f));
            }
            for (int k = 0; k < c.n; k++) worstKappa = Math.Max(worstKappa, Math.Abs(line.kappa[k]) - Math.Abs(c.curv[k]) - 0.01);
        }
        Check("RACE_LINE_STAYS_ON_THE_ROAD", worstOff <= 0.01, "worst overshoot of the 1.2 m margin " + worstOff.ToString("0.00") + " m, built in " + (buildMs / LineCircuits).ToString("0") + " ms per circuit", "inside the asphalt with a margin");
        Check("RACE_LINE_BEATS_THE_CENTRELINE", sumOpt < sumCentre * 0.97, "lap time on the line " + (sumOpt / LineCircuits).ToString("0.0") + " s, down the middle " + (sumCentre / LineCircuits).ToString("0.0") + " s (" + ((1 - sumOpt / sumCentre) * 100).ToString("0.0") + " % faster)", "at least 3 % faster than the centreline");
        Check("RACE_PLAN_RESPECTS_THE_BIKE", worstLimitOver < 0.01 && worstAccel < 0.5 && worstBrake < 0.5, "plan over the grip limit by " + worstLimitOver.ToString("0.000") + " m/s, accel over the measured by " + worstAccel.ToString("0.00") + ", braking over by " + worstBrake.ToString("0.00") + " m/s2", "no speed the bike cannot hold, reach or shed");
        Check("RACE_PLAN_PACE_IS_PLAUSIBLE", minAvg > 85 && maxAvg < 190, "average speed of the plan " + minAvg.ToString("0") + " to " + maxAvg.ToString("0") + " km/h, top speed of the bike " + (lines[0].curves.topSpeed * 3.6).ToString("0") + " km/h", "between 85 and 190 km/h");

        if (picturesDir != null)
            for (int i = 0; i < 4; i++) DrawLine(lines[i], Path.Combine(picturesDir, "line_" + i + ".png"));
        RaceSimChecks();
    }

    /// <summary>The circuit with the racing line coloured by planned speed: red slow, green fast.</summary>
    static void DrawLine(RacingLine line, string path)
    {
        var c = line.c;
        const int W = 1400, H = 1000;
        var pic = new Picture(W, H, 30, 40, 28);
        float mw = c.maxX - c.minX, mh = c.maxZ - c.minZ;
        float sc = Math.Min((W - 80f) / mw, (H - 80f) / mh);
        float ox = (W - mw * sc) * 0.5f - c.minX * sc, oy = (H - mh * sc) * 0.5f;
        Func<float, float> X = wx => ox + wx * sc;
        Func<float, float> Y = wz => H - (oy + (wz - c.minZ) * sc);
        for (int i = 0; i < c.n; i++)
            for (float d = -c.halfW[i]; d <= c.halfW[i]; d += 0.6f)
                pic.Disc(X(c.px[i] + c.nx[i] * d), Y(c.pz[i] + c.nz[i] * d), Math.Max(0.9f, sc * 1.0f), 80, 80, 86);
        float vmin = float.MaxValue, vmax = 0;
        for (int i = 0; i < c.n; i++) { vmin = Math.Min(vmin, line.vplan[i]); vmax = Math.Max(vmax, line.vplan[i]); }
        for (int i = 0; i < c.n; i++)
        {
            float t = (line.vplan[i] - vmin) / Math.Max(1f, vmax - vmin);
            byte r = (byte)(255 * Math.Min(1f, 2f * (1f - t))), g = (byte)(255 * Math.Min(1f, 2f * t));
            pic.Disc(X(line.x[i]), Y(line.z[i]), Math.Max(1.2f, sc * 1.1f), r, g, 40);
        }
        pic.Save(path);
    }
}
