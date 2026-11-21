using System;
using System.Collections.Generic;
using System.Globalization;
using PryingEyes.Pista;

/// <summary>The licence tests: the list must be well formed, the medal times must follow the racing line (so a computer rider at full pace earns gold, a slower one silver and a beginner bronze: a medal
/// is always within reach), a lap must be judged the same way at the edges, only a clean flying lap may count, and the player's book of times must keep the best, come back from the preferences whatever
/// the language of the machine and survive any text it is handed.</summary>
static partial class Program
{
    static bool IsAscii(string s) { foreach (char ch in s) if (ch > 127) return false; return true; }

    /// <summary>Ride a licence test alone with a computer rider at a pace, the way the game's race scores it (the first lap is the standing-start one, a lap with a fall does not count).</summary>
    static LicenceRun RideLicence(LicenceTest t, float pace, out int falls, out double plan)
    {
        var c = CircuitGen.Generate(t.Seed);
        var race = new Race(c, Licences.Laps, t.Seed);
        var r = race.AddAi("solo", t.bike, 0, 1, pace, 0f);
        race.PlaceAll(new List<Racer> { r });
        var run = new LicenceRun(t);
        int kind = 0, falls0 = 0;
        race.OnLap = rr => { bool counts; run.Lap(kind, rr.crashes == falls0, rr.lastLap, out counts); kind = 1; falls0 = rr.crashes; };
        race.StartCountdown();
        float tt = 0f;
        while (race.phase != RacePhase.Over && tt < 900f) { race.Step(RaceDt); tt += RaceDt; }
        falls = r.crashes;
        plan = race.LineFor(t.bike).lapTime;
        return run;
    }

    static void LicenceChecks()
    {
        var all = Licences.All;

        // ---- the list
        {
            var ids = new HashSet<string>(); var pairs = new HashSet<string>(); string bad = "";
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t.id.Length == 0 || !ids.Add(t.id)) bad += " id" + i;
                if (!pairs.Add(t.circuit + "/" + t.bike)) bad += " repeated" + i;
                if (t.name.Length < 3 || t.brief.Length < 20 || !IsAscii(t.name) || !IsAscii(t.brief) || !IsAscii(t.id)) bad += " text" + i;
                if (t.circuit < 0 || t.circuit >= Catalogue.Count) bad += " circuit" + i;
                if (t.bike < 0 || t.bike >= Bikes.Count) bad += " bike" + i;
                if (t.aids < 0 || t.aids > 2 || t.hour < 0 || t.hour > 2) bad += " rules" + i;
                if (t.licence != i / Licences.PerLicence) bad += " licence" + i;
            }
            bool names = Licences.Names.Length * Licences.PerLicence == all.Length && Licences.Taglines.Length == Licences.Names.Length;
            foreach (var n in Licences.Names) if (!IsAscii(n)) names = false;
            foreach (var n in Licences.Taglines) if (!IsAscii(n)) names = false;
            Check("LICENCE_TESTS_ARE_WELL_FORMED", all.Length == 9 && names && bad.Length == 0,
                all.Length + " tests in " + Licences.Names.Length + " licences" + (bad.Length == 0 ? ", keys unique, circuits and bikes in range, no pair repeated, texts in plain ASCII" : ", problems:" + bad),
                "nine tests, three licences of three, every key unique, every circuit a named one and every bike in the roster, plain ASCII");
        }

        // ---- the targets
        {
            string order = "", ratios = "";
            double[] gMin = { 9, 9, 9 }, gMax = { 0, 0, 0 };
            foreach (var t in all)
            {
                if (!(t.gold > 30f && t.gold + 2f <= t.silver && t.silver + 2f <= t.bronze)) order += " " + t.id;
                double plan = new Race(CircuitGen.Generate(t.Seed), 1, t.Seed).LineFor(t.bike).lapTime;
                double[] r = { t.gold / plan, t.silver / plan, t.bronze / plan };
                double[] want = { 1.08, 1.15, 1.27 };
                for (int k = 0; k < 3; k++)
                {
                    gMin[k] = Math.Min(gMin[k], r[k]); gMax[k] = Math.Max(gMax[k], r[k]);
                    if (Math.Abs(r[k] - want[k]) > 0.01) ratios += " " + t.id + "[" + k + "]=" + r[k].ToString("0.000", CultureInfo.InvariantCulture);
                }
            }
            Check("LICENCE_TARGETS_ARE_IN_ORDER", order.Length == 0, order.Length == 0 ? "gold < silver < bronze with at least two seconds between them, on every test" : "out of order:" + order,
                "gold before silver before bronze, two seconds apart at least");
            Func<int, string> band = k => gMin[k].ToString("0.000", CultureInfo.InvariantCulture) + "-" + gMax[k].ToString("0.000", CultureInfo.InvariantCulture);
            Check("LICENCE_TARGETS_FOLLOW_THE_PLANNED_LAP", ratios.Length == 0, ratios.Length == 0 ? "gold " + band(0) + ", silver " + band(1) + ", bronze " + band(2) + " times the lap the racing line plans" : "away from 1.08 / 1.15 / 1.27:" + ratios,
                "within 0.01 of 1.08, 1.15 and 1.27 times the planned lap (the targets are rounded to half a second): a change of the physics or of the generator must retune them");
        }

        // ---- a computer rider earns the medals
        {
            string bad = "", paces = "";
            double[] lo = { 9, 9, 9 }, hi = { 0, 0, 0 };
            float[] pace = { 1.00f, 0.97f, 0.90f };
            int totalFalls = 0;
            foreach (var t in all)
            {
                var medals = new Medal[3];
                for (int k = 0; k < 3; k++)
                {
                    int falls; double plan;
                    var run = RideLicence(t, pace[k], out falls, out plan);
                    medals[k] = run.medal; totalFalls += falls;
                    double ratio = run.bestLap / plan;
                    lo[k] = Math.Min(lo[k], ratio); hi[k] = Math.Max(hi[k], ratio);
                    if (falls > 0 || run.counted < 1) bad += " " + t.id + "@" + pace[k].ToString("0.00", CultureInfo.InvariantCulture) + "(falls " + falls + ", counted " + run.counted + ")";
                }
                if (!(medals[0] == Medal.Gold && medals[1] >= Medal.Silver && medals[2] >= Medal.Bronze && medals[2] < Medal.Gold)) bad += " " + t.id + "(" + medals[0] + "/" + medals[1] + "/" + medals[2] + ")";
            }
            for (int k = 0; k < 3; k++) paces += (k > 0 ? ", " : "") + pace[k].ToString("0.00", CultureInfo.InvariantCulture) + " -> " + lo[k].ToString("0.000", CultureInfo.InvariantCulture) + "-" + hi[k].ToString("0.000", CultureInfo.InvariantCulture);
            Check("LICENCE_COMPUTER_RIDERS_EARN_THE_MEDALS", bad.Length == 0 && totalFalls == 0,
                bad.Length == 0 ? "nine tests, three paces each, no falls; best lap against the plan: " + paces + " (gold at full pace, silver at 0.97 or better, bronze but not gold at 0.90)" : "problems:" + bad,
                "full pace earns gold, 0.97 silver or better, 0.90 bronze but not gold, without a fall, on every test");
        }

        // ---- the edges of a medal
        {
            var t = all[0];
            bool edges = t.Judge(t.gold) == Medal.Gold && t.Judge(t.gold + 0.01f) == Medal.Silver && t.Judge(t.silver) == Medal.Silver && t.Judge(t.silver + 0.01f) == Medal.Bronze
                && t.Judge(t.bronze) == Medal.Bronze && t.Judge(t.bronze + 0.01f) == Medal.None && t.Judge(t.gold - 5f) == Medal.Gold;
            bool nonsense = t.Judge(0f) == Medal.None && t.Judge(-5f) == Medal.None && t.Judge(float.NaN) == Medal.None && t.Judge(float.PositiveInfinity) == Medal.None && t.Judge(float.NegativeInfinity) == Medal.None;
            bool targets = t.Target(Medal.Gold) == t.gold && t.Target(Medal.Silver) == t.silver && t.Target(Medal.Bronze) == t.bronze && t.Target(Medal.None) == 0f
                && Licences.MedalName(Medal.Gold) == "ORO" && Licences.MedalName(Medal.Silver) == "ARGENTO" && Licences.MedalName(Medal.Bronze) == "BRONZO" && Licences.MedalName(Medal.None) == "";
            Check("LICENCE_JUDGE_AT_THE_EDGES", edges && nonsense && targets, "a time equal to a target earns its medal, a hundredth over earns the next; zero, negative, infinite and 'not a number' earn nothing",
                "at most the target earns the medal; a time that is not a time earns nothing");
        }

        // ---- what counts in a test
        {
            var t = all[3];
            var run = new LicenceRun(t); bool counts;
            Medal a = run.Lap(0, true, t.gold - 5f, out counts);
            bool standing = !counts && a == Medal.None && run.counted == 0 && run.bestLap == 0f && run.medal == Medal.None;
            Medal b = run.Lap(1, false, t.gold - 5f, out counts);
            bool fall = !counts && b == Medal.None && run.counted == 0;
            Medal c = run.Lap(1, true, t.bronze - 1f, out counts);
            bool first = counts && c == Medal.Bronze && run.medal == Medal.Bronze && run.bestLap == t.bronze - 1f && run.counted == 1;
            Medal d = run.Lap(1, true, t.bronze + 20f, out counts);
            bool slower = counts && d == Medal.None && run.medal == Medal.Bronze && run.bestLap == t.bronze - 1f && run.counted == 2;
            Medal e = run.Lap(1, true, t.silver - 0.5f, out counts);
            bool better = counts && e == Medal.Silver && run.medal == Medal.Silver && run.bestLap == t.silver - 0.5f && run.counted == 3;
            run.Lap(1, true, float.NaN, out counts); bool nan = !counts;
            run.Lap(1, true, 0f, out counts); bool zero = !counts;
            Medal f = run.Lap(1, true, t.silver + 2f, out counts);
            bool keeps = counts && f == Medal.Bronze && run.medal == Medal.Silver && run.bestLap == t.silver - 0.5f;
            Check("LICENCE_ONLY_A_CLEAN_FLYING_LAP_COUNTS", standing && fall && first && slower && better && nan && zero && keeps,
                "the standing-start lap and a lap with a fall are left out however quick, a slower lap does not take the medal back, the best lap is the one that stays",
                "only a flying lap with no fall counts; the medal of the attempt is the best lap's");
        }

        // ---- the book of times
        {
            var book = new LicenceBook();
            bool fresh = book.Count(Medal.Bronze) == 0 && !book.Obtained(0) && !book.Obtained(1) && !book.Obtained(2) && book.Serialize() == "" && book.BestOf(0) == 0f && book.MedalOf(0) == Medal.None;
            bool s1 = book.Submit(0, all[0].bronze - 1f) && book.MedalOf(0) == Medal.Bronze;
            bool s2 = !book.Submit(0, all[0].bronze - 1f) && !book.Submit(0, all[0].bronze + 9f) && book.BestOf(0) == all[0].bronze - 1f;
            bool s3 = book.Submit(0, all[0].gold - 0.5f) && book.MedalOf(0) == Medal.Gold;
            book.Submit(1, all[1].bronze - 0.5f);
            bool partial = !book.Obtained(0) && book.Count(Medal.Bronze) == 2 && book.Count(Medal.Gold) == 1 && book.Count(Medal.Silver) == 1;
            book.Submit(2, all[2].silver - 0.5f);
            bool obtained = book.Obtained(0) && !book.Obtained(1) && !book.Obtained(2) && book.Count(Medal.Bronze) == 3 && book.Count(Medal.Silver) == 2 && book.Count(Medal.Gold) == 1;
            bool slow = !book.Submit(3, all[3].bronze + 5f) || book.MedalOf(3) == Medal.None;
            bool outside = !book.Submit(-1, 50f) && !book.Submit(99, 50f) && book.BestOf(-1) == 0f && book.BestOf(99) == 0f && book.MedalOf(99) == Medal.None;
            bool nonsense = !book.Submit(4, 0f) && !book.Submit(4, -3f) && !book.Submit(4, float.NaN) && !book.Submit(4, float.PositiveInfinity) && book.BestOf(4) == 0f;
            Check("LICENCE_BOOK_KEEPS_THE_BEST", fresh && s1 && s2 && s3 && partial && obtained && slow && outside && nonsense,
                "a better lap replaces, a worse one does not; the medal follows the targets; three medals in a licence obtain it; a test that does not exist or a time that is not a time is refused",
                "the best time of every test, its medal worked out from it, licences obtained with a medal on each of their tests");
        }

        // ---- to and from the preferences
        {
            var book = new LicenceBook();
            book.Submit(0, 83.4167f); book.Submit(4, 112.0f); book.Submit(8, 129.499f);
            string text = book.Serialize();
            CultureInfo italianCulture = null;
            try { italianCulture = new CultureInfo("it-IT"); } catch (CultureNotFoundException) { }
            string italian = text, fromItalian = text; bool same = true, sameIt = true;
            if (italianCulture != null)
            {
                var keep = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = italianCulture;
                    italian = book.Serialize();
                    var backIt = LicenceBook.Parse(text);
                    for (int i = 0; i < Licences.Count; i++) sameIt &= Math.Abs(backIt.BestOf(i) - book.BestOf(i)) < 0.001f && backIt.MedalOf(i) == book.MedalOf(i);
                    fromItalian = backIt.Serialize();
                }
                finally { CultureInfo.CurrentCulture = keep; }
            }
            var back = LicenceBook.Parse(text);
            for (int i = 0; i < Licences.Count; i++) same &= Math.Abs(back.BestOf(i) - book.BestOf(i)) < 0.001f && back.MedalOf(i) == book.MedalOf(i);
            Check("LICENCE_BOOK_ROUNDTRIPS_IN_ANY_LANGUAGE", same && sameIt && italian == text && fromItalian == text && text.Contains("a1-1=83.417") && !text.Contains(","),
                "'" + text + "' comes back within a thousandth" + (italianCulture != null ? " and reads the same on a machine with decimal commas" : " (this machine has no Italian culture: the decimal comma was not exercised)"),
                "the preferences text uses a point, whatever the culture, and gives back the same times");
        }
        {
            string[] junk = { null, "", ";", "=", "a1-1", "a1-1=", "a1-1=abc", "a1-1=-3", "a1-1=0", "a1-1=NaN", "a1-1=Infinity", "a1-1=99999", "zzz=50", "a1-1=1,5", "é=1", "a1-1=90=80", "=90;=80;;==" };
            string leaked = ""; int thrown = 0;
            foreach (var j in junk)
            {
                try { var b = LicenceBook.Parse(j); if (b.Count(Medal.Bronze) != 0 || b.Serialize() != "") leaked += " [" + j + "]"; }
                catch (Exception) { thrown++; }
            }
            var kept = LicenceBook.Parse("a1-1=90;a1-1=80;a1-1=85");
            var trimmed = LicenceBook.Parse("  a2-2 = 100.5  ;;a-3=140");
            bool picks = Math.Abs(kept.BestOf(0) - 80f) < 0.001f && Math.Abs(trimmed.BestOf(4) - 100.5f) < 0.001f && Math.Abs(trimmed.BestOf(8) - 140f) < 0.001f && trimmed.BestOf(0) == 0f;
            Check("LICENCE_BOOK_SURVIVES_NONSENSE", thrown == 0 && leaked.Length == 0 && picks,
                junk.Length + " damaged texts: " + thrown + " threw, " + (leaked.Length == 0 ? "none left a time" : "left a time:" + leaked) + "; the best of repeated keys is kept and spaces are ignored",
                "nothing throws, nothing that is not a known test with a believable time is kept");
        }

        // ---- the rules of a test
        Check("LICENCE_RULES_LEAVE_A_FLYING_LAP", Licences.Laps >= 3 && Licences.Rivals == 0 && Licences.PerLicence == 3,
            Licences.Laps + " laps alone: the first is the standing-start one, the others fly", "at least three laps (one standing-start, two flying) and no rivals");
    }
}
