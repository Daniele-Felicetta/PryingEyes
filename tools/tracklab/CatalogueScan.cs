using System;
using System.Collections.Generic;
using System.Text;
using PryingEyes.Pista;

/// <summary>A development aid, not a check: `dotnet run -c Release -- --scan FROM TO` prints what every seed in the range looks like (length, hills, tight corners, longest straight, name), so the
/// circuits of the catalogue (Rules/Track/Catalogue.cs) can be picked by their character instead of by luck.</summary>
static partial class Program
{
    /// <summary>The numbers that describe the character of a circuit.</summary>
    public struct Character
    {
        public float length, hills, longestStraight;
        public int tightCorners, mediumCorners, hairpins;
        public string name;
    }

    public static Character Describe(Circuit c)
    {
        var ch = new Character { length = c.length, name = c.name };
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < c.n; i++) { lo = Math.Min(lo, c.py[i]); hi = Math.Max(hi, c.py[i]); }
        ch.hills = hi - lo;
        // runs of curvature: straight (radius over 1500 m), tight (under 140 m), hairpin (under 55 m)
        float straight = 0f, best = 0f, tightRun = 0f, minR = 1e9f;
        bool inTight = false;
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < c.n; i++)
            {
                float k = Math.Abs(c.curv[i]);
                if (k < 1f / 1500f) { straight += c.spacing; if (straight > best) best = straight; } else straight = 0f;
                if (pass == 0) continue;
                if (k > 1f / 140f) { tightRun += c.spacing; minR = Math.Min(minR, 1f / Math.Max(k, 1e-6f)); inTight = true; }
                else if (inTight)
                {
                    if (tightRun >= 25f) { ch.tightCorners++; if (minR < 55f) ch.hairpins++; }
                    else if (tightRun >= 10f) ch.mediumCorners++;
                    tightRun = 0f; minR = 1e9f; inTight = false;
                }
            }
        ch.longestStraight = best;
        return ch;
    }

    static void ScanSeeds(int from, int to)
    {
        var sb = new StringBuilder();
        sb.AppendLine("seed      length   hills  straight  tight  hairpins  name");
        for (int seed = from; seed <= to; seed++)
        {
            var c = Circuits.Generate(seed);
            var d = Describe(c);
            sb.AppendLine(seed.ToString().PadRight(9) + d.length.ToString("F0").PadLeft(6) + " m" + d.hills.ToString("F0").PadLeft(6) + " m" + d.longestStraight.ToString("F0").PadLeft(7) + " m" + d.tightCorners.ToString().PadLeft(6) + d.hairpins.ToString().PadLeft(9) + "    " + d.name);
        }
        Console.Write(sb.ToString());
    }
}
