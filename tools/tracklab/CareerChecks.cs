using System;
using System.Collections.Generic;
using System.Text;
using PryingEyes.Pista;

/// <summary>The money of the career (Career.cs): every bike has a price in the order of the classes, a race pays by its place, its laps, its field, its level and its bike, a bike is bought once and only with the money there is, the
/// book that the preferences keep reads back what was written and shrugs off what is not a book, and the first step up (a 600) is within reach of whoever wins the three tests of the first licence.</summary>
static partial class Program
{
    static void CareerChecks()
    {
        int n = Bikes.Count;
        int starter = Economy.StarterBike;

        // ---- every bike has a price, the starter is the cheapest, and a bigger class costs more
        var missing = new StringBuilder();
        int cheapest = 0;
        var byClass = new Dictionary<string, int[]>();   // lowest and highest price of each class
        for (int i = 0; i < n; i++)
        {
            int p = Economy.Price(i);
            if (p <= 0) missing.Append(Bikes.Get(i).name + " ");
            if (p < Economy.Price(cheapest)) cheapest = i;
            string k = Bikes.Get(i).klass;
            if (!byClass.ContainsKey(k)) byClass[k] = new[] { int.MaxValue, 0 };
            byClass[k][0] = Math.Min(byClass[k][0], p); byClass[k][1] = Math.Max(byClass[k][1], p);
        }
        bool classes = byClass["Superbike"][0] > byClass["Supersport"][1] && byClass["Hypersport"][0] > byClass["Superbike"][1] && byClass["Supersport"][0] > byClass["125 GP"][1] && byClass["Roadster"][0] > byClass["125 GP"][1];
        Check("CAREER_EVERY_BIKE_HAS_A_PRICE_IN_THE_ORDER_OF_THE_CLASSES", missing.Length == 0 && cheapest == starter && classes,
            missing.Length > 0 ? "no price for: " + missing : "nine prices, the " + Bikes.Get(starter).name + " is the cheapest (" + Economy.Price(starter) + "), a Superbike costs " + byClass["Superbike"][0] + "-" + byClass["Superbike"][1] + ", the Hypersport " + byClass["Hypersport"][1],
            "every bike priced, the starter the cheapest, Superbike above Supersport, Hypersport above Superbike");

        // ---- what a race pays
        int expert = 1;
        int win = Economy.Winnings(1, true, 3, 11, starter, expert);
        bool descending = true; int previous = int.MaxValue;
        for (int place = 1; place <= 12; place++)
        {
            int w = Economy.Winnings(place, true, 3, 11, starter, expert);
            if (w > previous || w <= 0) descending = false;
            previous = w;
        }
        bool nothing = Economy.Winnings(1, false, 3, 11, starter, expert) == 0 && Economy.Winnings(13, true, 3, 11, starter, expert) == 0 && Economy.Winnings(1, true, 3, Economy.MinRivals - 1, starter, expert) == 0 && Economy.Winnings(0, true, 3, 11, starter, expert) == 0;
        int twoLaps = Economy.Winnings(1, true, 6, 11, starter, expert);
        bool laps = Math.Abs(twoLaps - 2 * win) <= 10;
        bool bike = Economy.Winnings(1, true, 3, 11, 6, expert) > win;
        bool rank = Economy.Winnings(1, true, 3, 11, starter, 2) > win && Economy.Winnings(1, true, 3, 11, starter, 0) < win;
        bool field = Economy.Winnings(1, true, 3, 11, starter, expert) > Economy.Winnings(1, true, 3, 3, starter, expert);
        bool tens = true;
        for (int b = 0; b < n; b++) for (int place = 1; place <= 12; place++) if (Economy.Winnings(place, true, 3, 11, b, expert) % 10 != 0) tens = false;
        Check("CAREER_A_RACE_PAYS_BY_PLACE_LAPS_FIELD_LEVEL_AND_BIKE", descending && nothing && laps && bike && rank && field && tens,
            "a win pays " + win + " (3 laps, 11 rivals, expert, the " + Bikes.Get(starter).name + "), six laps " + twoLaps + ", the " + Bikes.Get(6).name + " " + Economy.Winnings(1, true, 3, 11, 6, expert) + ", champion " + Economy.Winnings(1, true, 3, 11, starter, 2)
            + ", beginner " + Economy.Winnings(1, true, 3, 11, starter, 0) + "; 3 rivals " + Economy.Winnings(1, true, 3, 3, starter, expert) + ", practice 0; the places go down from " + win + " to " + Economy.Winnings(12, true, 3, 11, starter, expert),
            "places go down, no prize for no finish, no rival or past twelfth, double laps double the prize, a bigger bike, a higher level and a bigger field pay more, whole tens");

        // ---- a new career
        var fresh = new CareerBook();
        bool onlyStarter = fresh.OwnedCount == 1 && fresh.Owns(starter) && fresh.Money == Economy.StartMoney;
        bool ownedOrFirst = fresh.OwnedOrFirst(0) == starter && fresh.OwnedOrFirst(starter) == starter && fresh.OwnedOrFirst(-5) == starter;
        bool next = fresh.NextOwned(0, 1) == starter && fresh.NextOwned(starter, 1) == starter && fresh.NextOwned(starter, -1) == starter;
        bool classOf = fresh.FirstOwnedOf("125 GP") == starter && fresh.FirstOwnedOf("Superbike") == -1;
        Check("CAREER_A_NEW_CAREER_HAS_THE_STARTER_AND_THE_STARTING_MONEY", onlyStarter && ownedOrFirst && next && classOf,
            "money " + fresh.Money + ", owns " + fresh.OwnedCount + " bike (" + Bikes.Get(starter).name + "); the first owned from a bike that is not owned is " + Bikes.Get(fresh.OwnedOrFirst(0)).name + "; no Superbike (" + fresh.FirstOwnedOf("Superbike") + ")",
            "the starting money, only the starter, and nothing else to step to");

        // ---- buying
        var book = new CareerBook();
        int target = 1;                                   // the Argo 600
        int price = Economy.Price(target);
        bool poor = !book.Buy(target) && !book.Owns(target) && book.Money == Economy.StartMoney && book.Missing(target) == price - Economy.StartMoney;
        bool twice = !book.Buy(starter) && book.Money == Economy.StartMoney;
        bool outside = !book.Buy(-1) && !book.Buy(n) && book.Money == Economy.StartMoney;
        book.Pay(price);
        int before = book.Money;
        bool bought = book.Buy(target) && book.Owns(target) && book.Money == before - price && book.OwnedCount == 2;
        bool again = !book.Buy(target) && book.Money == before - price;
        // two bikes owned (3 and 1): from either, the other is one step away in both directions
        bool steps = book.NextOwned(starter, 1) == target && book.NextOwned(starter, -1) == target && book.NextOwned(target, 1) == starter && book.NextOwned(target, -1) == starter;
        // a rich player buys the whole roster, and the money never goes below nothing or past the cap
        var rich = new CareerBook();
        for (int i = 0; i < 20; i++) rich.Pay(CareerBook.MaxMoney);
        bool cap = rich.Money == CareerBook.MaxMoney;
        bool all = true; int lowest = int.MaxValue;
        for (int i = 0; i < n; i++) { rich.Buy(i); lowest = Math.Min(lowest, rich.Money); all &= rich.Owns(i); }
        int wholeRoster = CareerBook.MaxMoney - rich.Money;
        int settled = rich.Money;
        rich.Pay(0); rich.Pay(-50);
        bool never = lowest >= 0 && rich.OwnedCount == n && rich.Money == settled;
        Check("CAREER_A_BIKE_IS_BOUGHT_ONCE_AND_ONLY_WITH_THE_MONEY_THERE_IS", poor && twice && outside && bought && again && steps && cap && all && never,
            "short of " + (price - Economy.StartMoney) + " the " + Bikes.Get(target).name + " is not bought (money " + Economy.StartMoney + " unchanged); with " + before + " it costs " + price + " and the money is " + book.Money + "; a second time changes nothing; the money tops out at " + CareerBook.MaxMoney + ", the whole roster costs "
            + wholeRoster + " and the money never goes below " + lowest,
            "short of money or already owned: nothing changes; bought: exactly the price, once; the money never goes negative or past the cap; a prize of nothing or less changes nothing");

        // ---- the book that the preferences keep
        var written = new CareerBook();
        written.Pay(1234); written.Pay(price + 1);
        written.Buy(target); written.Buy(7);
        string text = written.Serialize();
        var back = CareerBook.Parse(text);
        bool same = back.Money == written.Money && back.OwnedCount == written.OwnedCount;
        for (int i = 0; i < n; i++) same &= back.Owns(i) == written.Owns(i);
        bool again2 = back.Serialize() == text;
        var garbage = new[] { "", null, "nonsense", "m=;o=", "m=-5;o=-1", "m=abc;o=xyz", ";;;", "m=99999999999;o=999999999999", "x=1;y=2", "o=0", "m=100;o=0;m=bad", "=5;=6" };
        bool tough = true; var seen = new StringBuilder();
        foreach (var g in garbage)
        {
            CareerBook b = null;
            try { b = CareerBook.Parse(g); } catch (Exception e) { tough = false; seen.Append("[" + g + " threw " + e.GetType().Name + "] "); }
            if (b != null && !(b.Owns(starter) && b.Money >= 0 && b.Money <= CareerBook.MaxMoney)) { tough = false; seen.Append("[" + g + " -> money " + b.Money + ", starter " + b.Owns(starter) + "] "); }
        }
        var onlyMoney = CareerBook.Parse("m=500");
        var noStarter = CareerBook.Parse("m=500;o=" + (1 << 1));      // a book without the starter still has it
        bool partial = onlyMoney.Money == 500 && onlyMoney.OwnedCount == 1 && noStarter.Owns(starter) && noStarter.Owns(1);
        Check("CAREER_THE_BOOK_READS_BACK_WHAT_WAS_WRITTEN_AND_SHRUGS_OFF_THE_REST", same && again2 && tough && partial,
            tough ? "'" + text + "' reads back as the same money (" + back.Money + ") and the same " + back.OwnedCount + " bikes; twelve kinds of garbage leave a valid book; a book with only the money (500) and one without the starter still work" : seen.ToString(),
            "the same book after a round trip; garbage never throws and always leaves the starter and a believable amount");

        // ---- licence medals and cup trophies
        bool licences = Economy.LicenceBounty(Medal.None) == 0 && Economy.LicenceBounty(Medal.Bronze) < Economy.LicenceBounty(Medal.Silver) && Economy.LicenceBounty(Medal.Silver) < Economy.LicenceBounty(Medal.Gold);
        bool pay = Economy.LicencePay(Medal.None, Medal.Gold) == Economy.LicenceBounty(Medal.Gold)
            && Economy.LicencePay(Medal.Bronze, Medal.Gold) == Economy.LicenceBounty(Medal.Gold) - Economy.LicenceBounty(Medal.Bronze)
            && Economy.LicencePay(Medal.Gold, Medal.Bronze) == 0 && Economy.LicencePay(Medal.Silver, Medal.Silver) == 0 && Economy.LicencePay(Medal.None, Medal.None) == 0;
        int total = Economy.LicencePay(Medal.None, Medal.Bronze) + Economy.LicencePay(Medal.Bronze, Medal.Silver) + Economy.LicencePay(Medal.Silver, Medal.Gold);
        bool noLoop = total == Economy.LicenceBounty(Medal.Gold);                 // climbing medal by medal pays the same as one gold
        bool cups = Economy.CupBounty(Medal.None) == 0 && Economy.CupBounty(Medal.Bronze) < Economy.CupBounty(Medal.Silver) && Economy.CupBounty(Medal.Silver) < Economy.CupBounty(Medal.Gold);
        Check("CAREER_MEDALS_AND_TROPHIES_PAY_IN_ORDER_AND_A_BETTER_MEDAL_PAYS_THE_DIFFERENCE", licences && pay && noLoop && cups,
            "licence test: bronze " + Economy.LicenceBounty(Medal.Bronze) + ", silver " + Economy.LicenceBounty(Medal.Silver) + ", gold " + Economy.LicenceBounty(Medal.Gold) + " (bronze to gold pays " + Economy.LicencePay(Medal.Bronze, Medal.Gold) + ", gold to bronze 0); cup: bronze " + Economy.CupBounty(Medal.Bronze) + ", silver " + Economy.CupBounty(Medal.Silver) + ", gold " + Economy.CupBounty(Medal.Gold),
            "bronze < silver < gold, a lower medal pays nothing, climbing by steps pays the same as the top medal at once");

        // ---- the pace of the career (the design: these are my numbers, the check keeps them from drifting)
        int step = int.MaxValue;
        for (int i = 0; i < n; i++) if (i != starter) step = Math.Min(step, Economy.Price(i));
        int a1 = Economy.StartMoney + 3 * Economy.LicenceBounty(Medal.Gold) + Economy.LicenceBonus;
        bool firstStep = a1 >= step;                                              // three golds in the first licence buy the first step up
        double winShare = win / (double)step;
        bool pace = winShare >= 0.03 && winShare <= 0.12;                         // a win is a few per cent of the first step up: a dozen wins or so
        int roster = 0; for (int i = 0; i < n; i++) if (i != starter) roster += Economy.Price(i);
        int licencesAll = 9 * Economy.LicenceBounty(Medal.Gold) + 3 * Economy.LicenceBonus;
        bool grind = licencesAll < roster;                                        // the licences alone do not buy the whole roster
        Check("CAREER_THE_PACE_IS_A_DOZEN_WINS_FOR_THE_FIRST_STEP_UP", firstStep && pace && grind,
            "the first step up costs " + step + "; the start (" + Economy.StartMoney + ") plus three golds and the licence (" + (a1 - Economy.StartMoney) + ") is " + a1 + "; a win pays " + win + " = " + (winShare * 100.0).ToString("F1") + "% of it; all nine licences give " + licencesAll + " of the " + roster + " the whole roster costs",
            "three golds in A1 buy the cheapest step up; a win is 3-12% of it; the licences alone do not buy every bike");

        // ---- the way money is written
        bool fmt = Economy.Format(0) == "0" && Economy.Format(7) == "7" && Economy.Format(999) == "999" && Economy.Format(1000) == "1.000" && Economy.Format(3000) == "3.000" && Economy.Format(18500) == "18.500"
            && Economy.Format(1234567) == "1.234.567" && Economy.Format(9999999) == "9.999.999" && Economy.Format(-1500) == "-1.500";
        Check("CAREER_MONEY_IS_WRITTEN_WITH_A_POINT_BETWEEN_THE_THOUSANDS", fmt, "3000 -> " + Economy.Format(3000) + ", 18500 -> " + Economy.Format(18500) + ", 1234567 -> " + Economy.Format(1234567), "3.000, 18.500, 1.234.567");
    }
}
