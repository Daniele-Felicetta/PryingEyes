using System;
using System.Collections.Generic;
using System.Text;
using PryingEyes.Pista;

/// <summary>The workshop of the career (Tuning.cs, and the prices and levels in Career.cs): level 0 is the bike as it comes and the roster is never touched, every level is better than the last in the four ways it promises and by
/// what it promises, the prices follow the bike, the book tunes an owned bike once per level and only with the money there is and writes the levels down, a tuned bike is really quicker round a lap (its figures, its plan and a
/// lap ridden by the computer rider) and has a key and a line of its own, so that a record set with it is not mixed up with one set on the bike as it comes.</summary>
static partial class Program
{
    /// <summary>Is every number a level of tuning touches the same on two specs (and the key)?</summary>
    static bool SameTuned(BikeSpec a, BikeSpec b)
    {
        if (a.id != b.id || a.bikeMass != b.bikeMass || a.muFront != b.muFront || a.muRear != b.muRear || a.cdA != b.cdA || a.cdATuck != b.cdATuck || a.torqueCurve.Length != b.torqueCurve.Length) return false;
        for (int i = 0; i < a.torqueCurve.Length; i++) if (a.torqueCurve[i] != b.torqueCurve[i]) return false;
        return true;
    }

    /// <summary>Two laps alone on a circuit with the computer rider, on a bike of the roster or on a spec of its own: the best lap in seconds, and whether it was a clean ride (finished, no fall).</summary>
    static double AloneOn(Circuit circuit, int bike, BikeSpec spec, int seed, out bool clean)
    {
        var race = new Race(circuit, 2, 9);
        var r = race.AddAi("solo", bike, 0, seed, 0.96f, 0f, spec);
        race.PlaceAll(new List<Racer> { r });
        race.StartCountdown();
        float t = 0f;
        while (race.phase != RacePhase.Over && t < 500f) { race.Step(RaceDt); t += RaceDt; }
        clean = r.finished && r.crashes == 0;
        return r.bestLap > 0 ? r.bestLap : r.lastLap;
    }

    static void TuningChecks()
    {
        int n = Bikes.Count, top = Tuning.MaxLevel, starter = Economy.StarterBike;
        var circuits = new[] { CircuitGen.Generate(4242), CircuitGen.Generate(22) };

        // ---- level 0 is the bike as it comes, a copy never shares its torque curve with the roster, a level out of range is the nearest real one
        {
            bool zero = true, untouched = true, clamped = true;
            for (int i = 0; i < n; i++)
            {
                var stock = Bikes.Get(i);
                var keep = Bikes.Get(i);
                var keepTorque = (float[])keep.torqueCurve.Clone();
                zero &= ReferenceEquals(Tuning.Apply(stock, 0), stock) && SameTuned(Tuning.Of(i, 0), keep);
                bool own = true;
                for (int l = 1; l <= top; l++) own &= !ReferenceEquals(Tuning.Apply(stock, l).torqueCurve, stock.torqueCurve);
                var after = Bikes.Get(i);
                bool same = SameTuned(stock, keep) && SameTuned(after, keep);
                for (int k = 0; k < keepTorque.Length; k++) same &= stock.torqueCurve[k] == keepTorque[k] && after.torqueCurve[k] == keepTorque[k];
                untouched &= own && same;
                clamped &= SameTuned(Tuning.Of(i, top + 6), Tuning.Of(i, top)) && SameTuned(Tuning.Of(i, -2), keep);
            }
            Check("TUNING_LEVEL_ZERO_IS_THE_BIKE_AS_IT_COMES_AND_THE_ROSTER_IS_NEVER_TOUCHED", zero && untouched && clamped,
                n + " bikes: level 0 is the bike itself (" + (zero ? "yes" : "NO") + "), levels 1-" + top + " are copies with a torque curve of their own and the roster is the same after every bike was tuned at every level (" + (untouched ? "yes" : "NO") + "), level "
                + (top + 6) + " is level " + top + " and level -2 is level 0 (" + (clamped ? "yes" : "NO") + ")",
                "level 0 returns the bike; a copy never shares the curve with the roster; the roster is never changed; a level out of range is the nearest real one");
        }

        // ---- every level is better than the last in the four ways it promises, and by what it promises
        {
            bool better = true; string bad = "";
            double lowPower = 9, highPower = 0, lowCut = 9, highCut = 0;
            for (int i = 0; i < n; i++)
            {
                var stock = Bikes.Get(i);
                float rpm;
                float power0 = stock.PeakPowerKw(out rpm);
                var prev = stock;
                for (int l = 1; l <= top; l++)
                {
                    var s = Tuning.Of(i, l);
                    bool step = s.PeakPowerKw(out rpm) > prev.PeakPowerKw(out rpm) && s.muFront > prev.muFront && s.muRear > prev.muRear && s.cdA < prev.cdA && s.cdATuck < prev.cdATuck
                        && s.bikeMass < prev.bikeMass && s.riderMass == prev.riderMass && s.id == stock.id + "+" + l && s.tune == l;
                    if (!step) { better = false; bad += stock.id + "@" + l + " "; }
                    prev = s;
                }
                var full = Tuning.Of(i, top);
                double power = full.PeakPowerKw(out rpm) / power0 - 1.0;
                lowPower = Math.Min(lowPower, power); highPower = Math.Max(highPower, power);
                double cut = 1.0 - full.bikeMass / stock.bikeMass;
                lowCut = Math.Min(lowCut, cut); highCut = Math.Max(highCut, cut);
            }
            bool bounded = lowPower > 0.115 && highPower < 0.125 && lowCut > 0.055 && highCut < 0.065;
            Check("TUNING_EVERY_LEVEL_IS_BETTER_IN_FOUR_WAYS_BY_A_MEASURED_AMOUNT", better && bounded,
                better ? "level " + top + " gives " + (lowPower * 100.0).ToString("F1") + "-" + (highPower * 100.0).ToString("F1") + " % more peak power and " + (lowCut * 100.0).ToString("F1") + "-" + (highCut * 100.0).ToString("F1") + " % less bike weight on the nine bikes; every level has more power, grip and less drag and weight than the one before"
                    : "a level that is not better than the one before: " + bad,
                "each level: more power, more grip on both tyres, less drag (open and tucked), less weight, the rider unchanged; at the top 12 % more power and 6 % less weight");
        }

        // ---- the prices follow the bike
        {
            bool priced = true, whole = true, outside = true, follows = true;
            double lowShare = 9, highShare = 0;
            for (int i = 0; i < n; i++)
            {
                int a = Economy.TunePrice(i, 1), b = Economy.TunePrice(i, 2), c = Economy.TunePrice(i, 3);
                priced &= a > 0 && b > a && c > b;
                whole &= a % 10 == 0 && b % 10 == 0 && c % 10 == 0;
                outside &= Economy.TunePrice(i, 0) == 0 && Economy.TunePrice(i, top + 1) == 0 && Economy.TunePrice(i, -3) == 0;
                double all = (a + b + c) / (double)Economy.Price(i);
                lowShare = Math.Min(lowShare, all); highShare = Math.Max(highShare, all);
                for (int j = 0; j < n; j++) if (Economy.Price(i) < Economy.Price(j) && Economy.TunePrice(i, 3) > Economy.TunePrice(j, 3)) follows = false;
            }
            bool share = lowShare > 0.38 && highShare < 0.44;
            bool reach = Economy.TunePrice(starter, 1) + Economy.TunePrice(starter, 2) + Economy.TunePrice(starter, 3) <= Economy.StartMoney;       // the starter can be tuned right away
            Check("TUNING_THE_PRICES_FOLLOW_THE_BIKE_AND_THE_LEVEL", priced && whole && outside && follows && share && reach,
                "the " + Bikes.Get(starter).name + " asks " + Economy.TunePrice(starter, 1) + " + " + Economy.TunePrice(starter, 2) + " + " + Economy.TunePrice(starter, 3) + " cr, the " + Bikes.Get(n - 1).name + " " + Economy.TunePrice(n - 1, 1) + " + " + Economy.TunePrice(n - 1, 2) + " + " + Economy.TunePrice(n - 1, 3)
                + "; a full tune is " + (lowShare * 100.0).ToString("F1") + "-" + (highShare * 100.0).ToString("F1") + " % of the price of the bike",
                "a level costs more than the one before, in whole tens, nothing for a level that does not exist, a dearer bike costs more to tune, a full tune is about two fifths of the bike, and the starter can be fully tuned with the starting money");
        }

        // ---- the book tunes an owned bike once per level, and only with the money there is
        {
            int p1 = Economy.TunePrice(starter, 1), p2 = Economy.TunePrice(starter, 2), p3 = Economy.TunePrice(starter, 3);
            var book = new CareerBook();
            bool strangers = !book.Tune(1) && !book.Tune(-1) && !book.Tune(n) && book.Level(1) == 0 && book.Level(-1) == 0 && book.Level(n) == 0 && book.TuneMissing(1) == 0 && book.Money == Economy.StartMoney;
            var poor = CareerBook.Parse("m=100;o=" + (1 << starter));
            bool shortOf = !poor.Tune(starter) && poor.Level(starter) == 0 && poor.Money == 100 && poor.TuneMissing(starter) == p1 - 100;
            bool asNew = book.Level(starter) == 0 && book.TuneMissing(starter) == 0;
            int m0 = book.Money;
            bool one = book.Tune(starter) && book.Level(starter) == 1 && book.Money == m0 - p1;
            bool two = book.Tune(starter) && book.Level(starter) == 2 && book.Money == m0 - p1 - p2;
            bool three = book.Tune(starter) && book.Level(starter) == 3 && book.Money == m0 - p1 - p2 - p3;
            int left = book.Money;
            bool ceiling = !book.Tune(starter) && book.Level(starter) == 3 && book.Money == left && book.TuneMissing(starter) == 0;
            bool alone = book.OwnedCount == 1;
            for (int i = 0; i < n; i++) if (i != starter && book.Level(i) != 0) alone = false;
            Check("TUNING_A_BIKE_IS_TUNED_ONCE_PER_LEVEL_AND_ONLY_IF_OWNED_AND_PAID_FOR", strangers && shortOf && asNew && one && two && three && ceiling && alone,
                "a bike that is not owned is not tuned (money " + Economy.StartMoney + " unchanged); with 100 cr the first level of the " + Bikes.Get(starter).name + " is " + (p1 - 100) + " short; with " + m0 + " cr the three levels cost " + p1 + " + " + p2 + " + " + p3 + " and leave " + left
                + "; a fourth does nothing; no other bike changed",
                "not owned: nothing; short of money: nothing and the book says how much is missing; each level costs exactly its price; the top is the end; the other bikes stay as they were");
        }

        // ---- the levels are written down and read back, an old book has none, and what is not a level is shrugged off
        {
            var owner = CareerBook.Parse("m=30000;o=" + ((1 << starter) | (1 << 1) | (1 << 6)));
            owner.Tune(starter); owner.Tune(starter); owner.Tune(1); owner.Tune(6); owner.Tune(6); owner.Tune(6);
            string text = owner.Serialize();
            var back = CareerBook.Parse(text);
            bool round = back.Money == owner.Money && back.Serialize() == text;
            var digits = new StringBuilder();
            for (int i = 0; i < n; i++) { round &= back.Level(i) == owner.Level(i) && back.Owns(i) == owner.Owns(i); digits.Append((char)('0' + owner.Level(i))); }
            bool kept = back.Level(starter) == 2 && back.Level(1) == 1 && back.Level(6) == 3 && back.Level(0) == 0 && text.EndsWith(";u=" + digits);

            var old = CareerBook.Parse("m=3000;o=" + (1 << starter));
            bool oldBook = old.Money == 3000 && old.Owns(starter);
            for (int i = 0; i < n; i++) oldBook &= old.Level(i) == 0;

            var junk = new[] { "u=", "u=abc", "u=-1", "u=9999999999", "u=" + new string('9', n), "u=" + new string('3', n + 12), "m=100;u=;o=", "u=1.5", "u= 3 ", "o=" + (1 << starter) + ";u=١è", "u=;u=;u=", "=3;u" };
            bool shrugged = true; var seen = new StringBuilder();
            foreach (var j in junk)
            {
                try
                {
                    var b = CareerBook.Parse(j);
                    for (int i = 0; i < n; i++) if (b.Level(i) < 0 || b.Level(i) > top || (!b.Owns(i) && b.Level(i) != 0)) { shrugged = false; seen.Append("[" + j + " -> bike " + i + " level " + b.Level(i) + "] "); }
                }
                catch (Exception e) { shrugged = false; seen.Append("[" + j + " threw " + e.GetType().Name + "] "); }
            }

            // the tuning of a bike that is not owned is not kept, whichever comes first in the text, and does not come along when the bike is bought
            var lone = CareerBook.Parse("m=90000;o=" + (1 << starter) + ";u=" + new string('3', n));
            var swapped = CareerBook.Parse("u=" + new string('3', n) + ";m=90000;o=" + (1 << starter));
            bool dropped = lone.Level(starter) == 3 && lone.Level(0) == 0 && lone.Level(1) == 0 && swapped.Level(starter) == 3 && swapped.Level(0) == 0 && swapped.Level(1) == 0;
            lone.Buy(1);
            dropped &= lone.Owns(1) && lone.Level(1) == 0 && lone.TuneMissing(1) == 0;
            Check("TUNING_THE_BOOK_WRITES_THE_LEVELS_AND_READS_THEM_BACK_AND_AN_OLD_BOOK_HAS_NONE", round && kept && oldBook && shrugged && dropped,
                shrugged ? "'" + text + "' reads back as the same money, bikes and levels (the digits keep their zeros); an old book ('m=3000;o=8') has every bike as it came; " + junk.Length + " kinds of garbage leave levels between 0 and " + top + "; the levels of bikes that are not owned are dropped"
                    : seen.ToString(),
                "the same book after a round trip, leading zeros kept; no 'u' in the text means no tuning; garbage never throws and never leaves a level out of range or on a bike that is not owned");
        }

        // ---- a tuned bike is quicker: its figures, its plan on two circuits, and a lap ridden by the computer rider
        {
            bool figs = true; int topFaster = 0; string figBad = "";
            for (int i = 0; i < n; i++)
            {
                var f0 = BikeSheet.Of(i); var f3 = BikeSheet.Of(i, top);
                float gain = f3.hp / f0.hp;
                bool ok = BikeSheet.Of(i, 0).hp == f0.hp && BikeSheet.Of(i, top).hp == f3.hp && gain > 1.11f && gain < 1.13f && f3.t100 < f0.t100 && f3.topKmh >= f0.topKmh - 0.5f && f3.kg < f0.kg && f3.grip > f0.grip && f3.brake100 <= f0.brake100;
                if (f3.topKmh > f0.topKmh + 0.5f) topFaster++;
                if (!ok) { figs = false; figBad += " [" + Bikes.Get(i).name + " hp x" + gain.ToString("F3") + " 0-100 " + f0.t100.ToString("F2") + "->" + f3.t100.ToString("F2") + " top " + f0.topKmh.ToString("F0") + "->" + f3.topKmh.ToString("F0") + " stop " + f0.brake100.ToString("F1") + "->" + f3.brake100.ToString("F1") + "]"; }
            }
            figs &= topFaster >= n / 2;
            Check("TUNING_THE_FIGURES_OF_A_TUNED_BIKE_ARE_BETTER", figs,
                figs ? "level " + top + " on the nine bikes: more power, quicker 0-100, a shorter stop, lighter, more grip, and the top speed higher on " + topFaster + " of them (a bike that runs out of gears does not go faster)" : "figures that are not better:" + figBad,
                "power 11-13 % up, 0-100 quicker, 100-0 no longer, lighter, more grip, top speed never lower and higher on at least half of the bikes");
        }
        {
            bool plans = true; double lowGain = 9, highGain = 0, lowLimit = 9, highLimit = 0; string planBad = "";
            for (int i = 0; i < n; i++)
            {
                foreach (var c in circuits)
                {
                    double l0 = new RacingLine(c, Bikes.Get(i)).lapTime, l1 = new RacingLine(c, Tuning.Of(i, 1)).lapTime, l2 = new RacingLine(c, Tuning.Of(i, 2)).lapTime, l3 = new RacingLine(c, Tuning.Of(i, 3)).lapTime;
                    double gain = 1.0 - l3 / l0;
                    // a rider who uses all the grip of the tune: the plan with the friction of the tuned bike instead of the fixed one
                    var fullTune = Tuning.Of(i, top); var asItComes = Bikes.Get(i);
                    float ratio = 0.5f * (fullTune.muFront + fullTune.muRear) / (0.5f * (asItComes.muFront + asItComes.muRear));
                    double limit = 1.0 - new RacingLine(c, fullTune, 1.05f * ratio).lapTime / l0;
                    bool ok = l0 > l1 && l1 > l2 && l2 > l3 && gain > 0.003 && gain < 0.05 && limit > 0.020 && limit < 0.050;
                    lowGain = Math.Min(lowGain, gain); highGain = Math.Max(highGain, gain); lowLimit = Math.Min(lowLimit, limit); highLimit = Math.Max(highLimit, limit);
                    if (!ok) { plans = false; planBad += " [" + Bikes.Get(i).name + " " + c.seed + ": " + l0.ToString("F2") + " " + l1.ToString("F2") + " " + l2.ToString("F2") + " " + l3.ToString("F2") + "]"; }
                }
            }
            Check("TUNING_THE_PLANNED_LAP_GETS_QUICKER_AT_EVERY_LEVEL", plans,
                plans ? "at level " + top + " the lap the plan allows is " + (lowGain * 100.0).ToString("F1") + "-" + (highGain * 100.0).ToString("F1") + " % quicker with the fixed grip of the computer riders (the power, the weight and the drag), and " + (lowLimit * 100.0).ToString("F1") + "-"
                    + (highLimit * 100.0).ToString("F1") + " % for a rider who uses the grip of the tune too (nine bikes, two circuits); quicker at every level than at the one before" : "a plan that is not quicker or not in range:" + planBad,
                "each level quicker than the last; at the top 0.3-5 % with the fixed grip and 2-5 % with the grip of the tune: a tune is felt, and a bike two classes down does not become the one above");
        }
        {
            var c0 = circuits[0];
            bool ridden = true; double lowGain = 9, highGain = 0; string rideBad = "";
            for (int i = 0; i < n; i++)
            {
                bool cleanStock, cleanTuned;
                double stockLap = AloneOn(c0, i, null, 11 + i, out cleanStock), tunedLap = AloneOn(c0, i, Tuning.Of(i, top), 11 + i, out cleanTuned);
                double gain = 1.0 - tunedLap / stockLap;
                bool ok = cleanStock && cleanTuned && gain > 0.003 && gain < 0.050;
                lowGain = Math.Min(lowGain, gain); highGain = Math.Max(highGain, gain);
                if (!ok) { ridden = false; rideBad += " [" + Bikes.Get(i).name + ": " + stockLap.ToString("F2") + " -> " + tunedLap.ToString("F2") + (cleanStock && cleanTuned ? "" : " FELL") + "]"; }
            }
            Check("TUNING_A_TUNED_BIKE_REALLY_LAPS_QUICKER_IN_A_RACE", ridden,
                ridden ? "the same computer rider with the same seed, two laps alone: the tuned bike is " + (lowGain * 100.0).ToString("F1") + "-" + (highGain * 100.0).ToString("F1") + " % quicker on every one of the nine bikes, with no fall" : "a ridden lap that is not quicker or not clean:" + rideBad,
                "the tuned bike laps 0.3-5 % quicker than the same bike as it comes, clean, on every bike (the computer rider plans with a fixed grip, so it only feels the power, the weight and the drag)");
        }

        // ---- a full tune does not make a bike silly: no loop at a launch, no fall, not past its limiter, and still a believable top gear
        {
            var flat = new FlatGround();
            bool all = true; string detail = "";
            for (int i = 0; i < n; i++)
            {
                var sp = Tuning.Of(i, top);
                float vmax = 0f, maxPitch = 0f, maxRpm = 0f; int topGear = 0; bool crashed = false;
                Ride(sp, flat, 70f, (t, b) => Full(), (t, b) =>
                {
                    vmax = Math.Max(vmax, b.u * 3.6f); maxPitch = Math.Max(maxPitch, b.freePitch); maxRpm = Math.Max(maxRpm, b.rpm); topGear = Math.Max(topGear, b.gear); crashed |= b.crashed;
                });
                bool ok = !crashed && maxPitch < 0.30f && maxRpm < sp.limiterRpm + 700f && topGear >= sp.GearCount - 2;
                all &= ok;
                detail += " " + Bikes.Get(i).name + " " + vmax.ToString("F0") + " km/h, front up " + (maxPitch * PM.Rad2Deg).ToString("F1") + " deg" + (ok ? ";" : " BAD (fell " + crashed + ", " + maxRpm.ToString("F0") + " rpm, gear " + topGear + ");");
            }
            Check("TUNING_A_FULL_TUNE_STILL_LAUNCHES_WITHOUT_A_LOOP_OR_A_FALL", all, "full throttle for 70 s at level " + top + ":" + detail, "no fall, the front wheel stays under 0.30 rad, the engine stays within 700 rpm of its limiter, the top gears are reached, on every bike");
        }

        // ---- a tuned bike has a key and a line of its own, and the rider on it really has the lighter bike
        {
            var seenIds = new HashSet<string>(); bool distinct = true;
            for (int i = 0; i < n; i++) for (int l = 0; l <= top; l++) distinct &= seenIds.Add(Tuning.Of(i, l).id);
            bool keyed = Tuning.Of(1, 0).id == Bikes.Get(1).id && Tuning.Of(1, 0).tune == 0 && Tuning.Of(1, top).tune == top && Tuning.Of(1, top).id == Bikes.Get(1).id + "+" + top && Tuning.Of(7, 2).id == Bikes.Get(7).id + "+2";
            bool words = Tuning.Label(0) == "" && Tuning.Label(-1) == "" && Tuning.Label(2) == "livello 2" && Tuning.Label(top + 5) == "livello " + top;
            var race = new Race(circuits[1], 2, 9);
            var tuned = Tuning.Of(1, top);
            var me = race.AddHuman("me", 1, 0, 5, tuned);
            var twin = race.AddHuman("twin", 1, 0, 5);
            bool mine = me.bike.spec.id == Bikes.Get(1).id + "+" + top && twin.bike.spec.id == Bikes.Get(1).id && me.bike.spec.Mass < twin.bike.spec.Mass && me.bikeIndex == twin.bikeIndex;
            bool lines = ReferenceEquals(race.LineFor(tuned), race.LineFor(Tuning.Of(1, top))) && !ReferenceEquals(race.LineFor(tuned), race.LineFor(1)) && race.LineFor(tuned).lapTime < race.LineFor(1).lapTime && ReferenceEquals(race.LineFor(1), race.LineFor(1)) && ReferenceEquals(race.LineFor(1), race.LineFor(Bikes.Get(1)));
            Check("TUNING_A_TUNED_BIKE_HAS_ITS_OWN_KEY_AND_ITS_OWN_LINE", distinct && keyed && words && mine && lines,
                (n * (top + 1)) + " bikes and levels, " + seenIds.Count + " different keys (" + Tuning.Of(1, top).id + "); the rider on it weighs " + me.bike.spec.Mass.ToString("F1") + " kg with the bike against " + twin.bike.spec.Mass.ToString("F1") + " as it comes; its line plans " + race.LineFor(tuned).lapTime.ToString("F2") + " s against "
                + race.LineFor(1).lapTime.ToString("F2") + "; \"" + Tuning.Label(2) + "\"",
                "every bike and level has its own key (a record set tuned is not one set stock), the human rider gets the spec he was given, the line is kept by that key, and the level is written 'livello N'");
        }
    }
}
