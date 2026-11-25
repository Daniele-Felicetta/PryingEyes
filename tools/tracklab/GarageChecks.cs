using System;
using System.Collections.Generic;
using System.Text;
using PryingEyes.Pista;

/// <summary>The figures of the garage (BikeSheet.cs): every bike ridden on a flat road gives numbers a bike of its kind would give, the numbers are the same every time, the bars tell the bikes apart and put them
/// in the order a person expects (the 125 is the lightest and the slowest, the 1300 the fastest and the hardest to turn).</summary>
static partial class Program
{
    static void GarageChecks()
    {
        int n = Bikes.Count;
        var figures = new BikeFigures[n];
        for (int i = 0; i < n; i++) figures[i] = BikeFigures.Ride(Bikes.Get(i));

        // ---- the figures of each bike are those of a bike of its kind
        var bad = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            var f = figures[i];
            bool ok = f.hp > 40f && f.hp < 230f && f.torque > 20f && f.torque < 200f && f.kg > 120f && f.kg < 340f && f.topKmh > 150f && f.topKmh < 330f && f.t100 > 2.4f && f.t100 < 6.0f && f.brake100 > 28f && f.brake100 < 52f;
            if (!ok) bad.Append(Bikes.Get(i).name + " (" + f.hp.ToString("F0") + " hp, " + f.kg.ToString("F0") + " kg, " + f.topKmh.ToString("F0") + " km/h, 0-100 " + f.t100.ToString("F2") + " s, 100-0 " + f.brake100.ToString("F1") + " m) ");
        }
        Check("GARAGE_FIGURES_ARE_THOSE_OF_REAL_BIKES", bad.Length == 0, bad.Length == 0 ? "nine bikes: " + Range(figures, f => f.topKmh, "F0") + " km/h, 0-100 " + Range(figures, f => f.t100, "F2") + " s, 100-0 " + Range(figures, f => f.brake100, "F1") + " m" : bad.ToString(),
            "40-230 hp, 20-200 Nm, 120-340 kg, 150-330 km/h, 0-100 in 2.4-6 s, 28-52 m to stop");

        // ---- ridden twice, the same
        bool same = true;
        for (int i = 0; i < n && same; i++)
        {
            var again = BikeFigures.Ride(Bikes.Get(i));
            var a = figures[i];
            same = a.hp == again.hp && a.torque == again.torque && a.kg == again.kg && a.topKmh == again.topKmh && a.t100 == again.t100 && a.brake100 == again.brake100 && a.agility == again.agility && a.grip == again.grip;
        }
        Check("GARAGE_FIGURES_ARE_THE_SAME_EVERY_TIME", same, same ? "nine bikes ridden twice, every number identical" : "a figure changed between two rides", "identical");

        // ---- the remembered figures are the ridden ones
        bool cached = true;
        for (int i = 0; i < n && cached; i++) cached = BikeSheet.Of(i).topKmh == figures[i].topKmh && BikeSheet.Of(i).t100 == figures[i].t100;
        Check("GARAGE_REMEMBERED_FIGURES_ARE_THE_RIDDEN_ONES", cached, "Of(i) = Ride(Bikes.Get(i)) for the nine bikes", "equal");

        // ---- the bars are in range and tell the bikes apart
        bool inRange = true; int bars = BikeSheet.BarCount;
        var lo = new float[bars]; var hi = new float[bars];
        for (int b = 0; b < bars; b++) { lo[b] = 2f; hi[b] = -1f; }
        for (int i = 0; i < n; i++)
            for (int b = 0; b < bars; b++)
            {
                float v = BikeSheet.Value(figures[i], (BikeBar)b);
                if (!(v >= 0f && v <= 1f)) inRange = false;
                lo[b] = Math.Min(lo[b], v); hi[b] = Math.Max(hi[b], v);
            }
        var flat = new StringBuilder();
        for (int b = 0; b < bars; b++) if (hi[b] - lo[b] < 0.25f) flat.Append(BikeSheet.BikeBarNames[b] + " ");
        Check("GARAGE_BARS_ARE_IN_RANGE_AND_TELL_THE_BIKES_APART", inRange && flat.Length == 0, flat.Length > 0 ? "bars too alike: " + flat : inRange ? "seven bars, each spreads over at least a quarter of the scale" : "a bar left 0..1", "0..1, spread of 0.25 or more");

        // ---- no two bikes draw the same picture
        float closest = float.MaxValue; string pair = "";
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                float d = 0f;
                for (int b = 0; b < bars; b++) d += Math.Abs(BikeSheet.Value(figures[i], (BikeBar)b) - BikeSheet.Value(figures[j], (BikeBar)b));
                if (d < closest) { closest = d; pair = Bikes.Get(i).name + " / " + Bikes.Get(j).name; }
            }
        Check("GARAGE_NO_TWO_BIKES_LOOK_ALIKE", closest >= 0.3f, "the closest pair is " + pair + ": the bars differ by " + closest.ToString("F2") + " in all", "0.3 or more");

        // ---- the order a person expects
        int lightest = 0, slowest = 0, fastest = 0, mostAgile = 0, leastAgile = 0, strongest = 0;
        for (int i = 1; i < n; i++)
        {
            if (figures[i].kg < figures[lightest].kg) lightest = i;
            if (figures[i].topKmh < figures[slowest].topKmh) slowest = i;
            if (figures[i].topKmh > figures[fastest].topKmh) fastest = i;
            if (figures[i].agility > figures[mostAgile].agility) mostAgile = i;
            if (figures[i].agility < figures[leastAgile].agility) leastAgile = i;
            if (figures[i].hp > figures[strongest].hp) strongest = i;
        }
        // the 125 (3) is the lightest, the slowest and the most agile; the 1300 (6) the fastest or nearly (the GP 500 is the other candidate), the strongest and the hardest to turn
        bool order = lightest == 3 && slowest == 3 && mostAgile == 3 && leastAgile == 6 && (fastest == 6 || fastest == 7) && (strongest == 6 || strongest == 7 || strongest == 0);
        Check("GARAGE_THE_ORDER_MAKES_SENSE", order, "lightest " + Bikes.Get(lightest).name + ", slowest " + Bikes.Get(slowest).name + ", fastest " + Bikes.Get(fastest).name + ", strongest " + Bikes.Get(strongest).name + ", most agile " + Bikes.Get(mostAgile).name + ", least agile " + Bikes.Get(leastAgile).name,
            "the 125 lightest, slowest and most agile; the 1300 the hardest to turn");

        // ---- every bike has its sentence and the word for its handling
        bool words = BikeSheet.BlurbCount == n;
        for (int i = 0; i < n && words; i++)
        {
            string s = BikeSheet.Blurb(i);
            words = !string.IsNullOrEmpty(s) && s.Length > 40 && s.Length < 170 && BikeSheet.HandlingWord(figures[i]).Length > 3;
        }
        Check("GARAGE_EVERY_BIKE_HAS_ITS_SENTENCE", words, words ? "nine sentences of 40 to 170 characters and a word for the handling of each" : "a sentence is missing, too short or too long", "nine sentences");
    }

    static string Range(BikeFigures[] f, Func<BikeFigures, float> pick, string format)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var x in f) { float v = pick(x); lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        return lo.ToString(format) + "-" + hi.ToString(format);
    }
}
