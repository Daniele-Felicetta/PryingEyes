using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PryingEyes.Pista;
using Production.Racing;

/// <summary>The championship (Championship.cs): the calendars, the table of points, a field of rivals that is the same all season, a season that adds up, the book that keeps the seasons and the trophies and survives damaged
/// text, and a season ridden by the computer on the real circuits (a quicker rider scores more).</summary>
static partial class Program
{
    /// <summary>The names the game draws its riders from (PistaDirector.Names): the lab needs only that there are enough of them.</summary>
    static readonly string[] CupNames =
    {
        "Valli", "Ferri", "Bosi", "Riva", "Neri", "Serra", "Costa", "Longo", "Moro", "Fanti", "Gallo", "Conti", "Sala", "Lenzi", "Vitale", "Pace",
        "Orsi", "Greco", "Monti", "Rota", "Pellini", "Zanetti", "Barbieri", "Cattaneo", "Ruggeri",
    };

    static void CupChecks()
    {
        // ---- the calendars
        {
            var problems = new StringBuilder();
            var ids = new HashSet<string>(); var titles = new HashSet<string>();
            foreach (var cup in Cups.All)
            {
                if (!ids.Add(cup.id) || !titles.Add(cup.name)) problems.Append(cup.id + " repeats an id or a name; ");
                if (cup.Rounds != 4 || cup.laps != 3) problems.Append(cup.id + " has " + cup.Rounds + " rounds of " + cup.laps + " laps; ");
                var seen = new HashSet<int>();
                for (int r = 0; r < cup.Rounds; r++)
                {
                    int c = cup.circuits[r];
                    if (c < 0 || c >= Catalogue.Count) problems.Append(cup.id + " round " + (r + 1) + " has no circuit " + c + "; ");
                    else if (!seen.Add(c)) problems.Append(cup.id + " rides circuit " + c + " twice; ");
                    else if (cup.CircuitSeed(r) != Catalogue.All[c].seed) problems.Append(cup.id + " round " + (r + 1) + " takes the wrong seed; ");
                }
                if (Cups.BikesOf(cup).Length == 0) problems.Append(cup.id + " has no bike of class " + cup.klass + "; ");
                else foreach (int b in Cups.BikesOf(cup)) if (!Cups.Allows(cup, b)) problems.Append(cup.id + " does not allow its own bike " + b + "; ");
                if (Cups.Allows(cup, -1) || Cups.Allows(cup, Bikes.Count)) problems.Append(cup.id + " allows a bike that does not exist; ");
                if (cup.tagline.Length < 30 || cup.tagline.Length > 160) problems.Append(cup.id + " has a tagline of " + cup.tagline.Length + " characters; ");
            }
            if (Cups.Count != 3) problems.Append(Cups.Count + " cups; ");
            Check("CUP_CALENDARS_ARE_WELL_FORMED", problems.Length == 0,
                problems.Length == 0 ? "three cups (" + string.Join(", ", Cups.All.Select(c => c.name + " " + c.klass + " " + string.Join("-", c.circuits))) + "), four rounds and three laps each, on different named circuits, every one for a class that has bikes" : problems.ToString(),
                "3 cups of 4 rounds and 3 laps, distinct named circuits, a class with bikes");
        }

        // ---- the points
        {
            var t = Cups.PointsTable;
            bool table = t.Length == 15 && t[0] == 25 && t[14] == 1;
            for (int i = 1; i < t.Length; i++) if (t[i] >= t[i - 1]) table = false;
            bool edges = Cups.PointsFor(1, true) == 25 && Cups.PointsFor(15, true) == 1 && Cups.PointsFor(16, true) == 0 && Cups.PointsFor(3, false) == 0 && Cups.PointsFor(0, true) == 0 && Cups.PointsFor(-4, true) == 0;
            int grid = 0;
            for (int p = 1; p <= Cups.Riders; p++) grid += Cups.PointsFor(p, true);
            Check("CUP_POINTS_FOLLOW_THE_TABLE", table && edges && grid == 134,
                "25 down to 1 over fifteen places, strictly falling; a rider who does not cross the line or is below the table scores 0; a grid of twelve hands out " + grid + " points and a season of four rounds " + 4 * grid,
                "25, 20, 16, 13, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1");
        }

        // ---- the field of rivals is the same all season
        {
            var problems = new StringBuilder();
            var firstNames = new List<string>();
            foreach (var cup in Cups.All)
            {
                var a = CupField.Make(cup, 1, 7, CupNames, 12);
                var b = CupField.Make(cup, 1, 7, CupNames, 12);
                bool same = a.Length == Cups.Rivals && b.Length == Cups.Rivals;
                for (int i = 0; same && i < a.Length; i++) same = a[i].name == b[i].name && a[i].bike == b[i].bike && a[i].skin == b[i].skin && a[i].pace == b[i].pace && a[i].mistakes == b[i].mistakes;
                if (!same) { problems.Append(cup.id + " is not the same field twice; "); continue; }
                firstNames.Add(string.Join(",", a.Select(r => r.name)));
                if (a.Select(r => r.name).Distinct().Count() != Cups.Rivals) problems.Append(cup.id + " repeats a name; ");
                if (a.Select(r => r.skin).Distinct().Count() != Cups.Rivals || a.Any(r => r.skin == 7 || r.skin < 0 || r.skin > 11)) problems.Append(cup.id + " repeats a livery or wears the free one; ");
                if (a.Any(r => !Cups.Allows(cup, r.bike))) problems.Append(cup.id + " has a rival on a bike of another class; ");

                // the level moves how fast they are, not who they are
                var lo = CupField.Make(cup, 0, 7, CupNames, 12); var hi = CupField.Make(cup, 2, 7, CupNames, 12);
                bool who = true;
                for (int i = 0; i < a.Length; i++) who &= a[i].name == lo[i].name && a[i].name == hi[i].name && a[i].bike == lo[i].bike && a[i].bike == hi[i].bike && a[i].skin == lo[i].skin && a[i].skin == hi[i].skin;
                if (!who) problems.Append(cup.id + " changes who rides with the level; ");
                double mLo = lo.Average(r => r.pace), mMid = a.Average(r => r.pace), mHi = hi.Average(r => r.pace);
                if (!(mLo < mMid && mMid < mHi)) problems.Append(cup.id + " paces do not grow with the level (" + mLo.ToString("0.000") + ", " + mMid.ToString("0.000") + ", " + mHi.ToString("0.000") + "); ");
                for (int rank = 0; rank < 3; rank++)
                    foreach (var r in CupField.Make(cup, rank, 7, CupNames, 12))
                        if (Math.Abs(r.pace - CupField.BasePace(rank)) > CupField.Spread(rank) + 1e-5f || r.mistakes != CupField.Mistakes(rank)) problems.Append(cup.id + " pace out of the band of level " + rank + "; ");
                // the colour the player wears moves only the colours
                var other = CupField.Make(cup, 1, 3, CupNames, 12);
                bool onlySkins = true;
                for (int i = 0; i < a.Length; i++) onlySkins &= a[i].name == other[i].name && a[i].bike == other[i].bike && a[i].pace == other[i].pace && other[i].skin != 3;
                if (!onlySkins) problems.Append(cup.id + " changes more than the colours with the player's livery; ");
            }
            if (firstNames.Distinct().Count() != firstNames.Count) problems.Append("two cups have the same field; ");
            Check("CUP_FIELD_IS_THE_SAME_ALL_SEASON", problems.Length == 0,
                problems.Length == 0 ? "eleven rivals per cup, the same twice, with their own names, colours (never the free one) and bikes of the class; the level of the season moves only the pace; each cup has its own field" : problems.ToString(),
                "the same field every round, whoever rides it");
        }

        // ---- a season adds up
        {
            var cup = Cups.All[1];
            var p = new CupProgress(cup);
            int[] r1 = { 5, 2, 0, 1, 3, 4, 6, 7, 8, 9, 10, 11 };          // the player third
            int[] r2 = { 0, 5, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11 };          // the player first
            int[] r3 = { 2, 0, 5, 1, 3, 4, 6, 7, 8, 9, 10, 11 };          // the player second
            int[] r4 = { 5, 2, 1, 3, 4, 6, 7, 8, 9, 10, 11, 0 };          // the player last, and he did not finish
            p.Record(r1, 12); p.Record(r2, 12); p.Record(r3, 12);
            bool midway = !p.Finished && p.round == 3 && p.NextRound == 3 && p.Trophy() == Medal.None;
            p.Record(r4, 11);
            int[] want = { 61, 58, 78, 46, 41, 86, 37, 33, 29, 25, 21, 17 };
            bool points = p.points.SequenceEqual(want);
            int[] wantOrder = { 5, 2, 0, 1, 3, 4, 6, 7, 8, 9, 10, 11 };
            bool order = p.Order().SequenceEqual(wantOrder) && p.Position(0) == 3 && p.Position(5) == 1 && p.Position(11) == 12;
            bool counts = p.wins[5] == 2 && p.wins[0] == 1 && p.wins[2] == 1 && p.seconds[2] == 2 && p.seconds[5] == 1 && p.seconds[0] == 1 && p.thirds[1] == 2 && p.thirds[0] == 1 && p.thirds[5] == 1;
            bool over = p.Finished && p.round == 4 && p.NextRound == 3 && p.Trophy() == Medal.Bronze;
            int before = p.points.Sum();
            p.Record(r1, 12);
            bool closed = p.round == 4 && p.points.Sum() == before;
            // the order of the standings when the points are the same: wins, then second places, then third places, then the field
            var tie = new CupProgress(cup);
            tie.points[3] = tie.points[7] = tie.points[9] = tie.points[10] = 50;
            tie.wins[3] = 1; tie.wins[7] = 2; tie.wins[9] = 1; tie.wins[10] = 1;
            tie.seconds[9] = 1; tie.thirds[10] = 4;
            var tieOrder = tie.Order().Take(4).ToArray();
            bool ties = tieOrder.SequenceEqual(new[] { 7, 9, 10, 3 });          // 7 has two wins, 9 one win and a second, 10 one win and four thirds, 3 one win only
            Check("CUP_SEASON_ADDS_UP", midway && points && order && counts && over && closed && ties,
                "four rounds with the player third, first, second and (not finishing) last give " + p.points[0] + " points against " + p.points[5] + " for the leader: points " + points + ", order " + order + ", wins and places " + counts + ", trophy bronze at the end " + over + ", a fifth round changes nothing " + closed + ", the ties " + ties + " (" + string.Join(",", tieOrder) + ")",
                "the points of the table, the standings by points then wins then second and third places, bronze for third");
        }

        // ---- the book keeps the seasons and the trophies
        CupBook book;
        {
            book = new CupBook();
            bool none = book.Season(0) == null && book.BestPlace(0) == 0 && book.BestTrophy(0) == Medal.None && book.Trophies(Medal.Bronze) == 0 && book.SeasonsDone(0) == 0 && !book.Record(0, new[] { 0, 1 }, 2);
            int[] win = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            var s = book.Begin(0, 1, 4);
            bool closedAt = true;
            for (int r = 0; r < s.cup.Rounds; r++) { bool closes = book.Record(0, win, 12); if (closes != (r == s.cup.Rounds - 1)) closedAt = false; }
            bool gold = closedAt && book.BestPlace(0) == 1 && book.SeasonsDone(0) == 1 && book.BestTrophy(0) == Medal.Gold && s.Trophy() == Medal.Gold && book.Trophies(Medal.Gold) == 1 && !book.Record(0, win, 12);
            // a new season keeps the trophy; a worse one does not take it away
            var s2 = book.Begin(0, 2, 5);
            bool fresh = book.Season(0) == s2 && s2.round == 0 && s2.rank == 2 && s2.freeSkin == 5 && book.BestPlace(0) == 1 && book.SeasonsDone(0) == 1;
            int[] fifth = { 1, 2, 3, 4, 0, 5, 6, 7, 8, 9, 10, 11 };
            for (int r = 0; r < s2.cup.Rounds; r++) book.Record(0, fifth, 12);
            bool worse = s2.Finished && s2.Position(0) == 5 && book.BestPlace(0) == 1 && book.SeasonsDone(0) == 2 && book.BestTrophy(0) == Medal.Gold && s2.Trophy() == Medal.None;
            // another cup, third place: bronze, and the count of trophies by level
            var s3 = book.Begin(2, 0, 0);
            int[] third = { 1, 2, 0, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            for (int r = 0; r < s3.cup.Rounds; r++) book.Record(2, third, 12);
            bool bronze = s3.Position(0) == 3 && book.BestTrophy(2) == Medal.Bronze && book.Trophies(Medal.Bronze) == 2 && book.Trophies(Medal.Silver) == 1 && book.Trophies(Medal.Gold) == 1;
            // a season midway in the middle cup, to be written down and read back
            var s4 = book.Begin(1, 1, 9);
            book.Record(1, third, 12); book.Record(1, fifth, 11);
            bool midway = s4.round == 2 && !s4.Finished && book.BestPlace(1) == 0;
            Check("CUP_BOOK_KEEPS_THE_SEASON_AND_THE_TROPHIES", none && gold && fresh && worse && bronze && midway,
                "nothing at first " + none + "; a season won every round: gold, closed by the last round only " + gold + "; a new season starts blank and the gold stays " + fresh + "; a fifth place does not take it away " + worse + "; third place in another cup: bronze, 2 trophies, 1 silver or better, 1 gold " + bronze + "; a season midway " + midway,
                "trophies are kept for good, a season is replaced only by a new one");
        }

        // ---- the book as text
        {
            string text = book.Serialize();
            var back = CupBook.Parse(text);
            bool same = back.Serialize() == text && text.Length > 40 && text.All(ch => ch < 128 && ch != ' ' && ch != '\n');
            for (int i = 0; i < Cups.Count && same; i++)
            {
                var a = book.Season(i); var b = back.Season(i);
                if ((a == null) != (b == null)) same = false;
                else if (a != null) same = a.rank == b.rank && a.round == b.round && a.freeSkin == b.freeSkin && a.points.SequenceEqual(b.points) && a.wins.SequenceEqual(b.wins) && a.seconds.SequenceEqual(b.seconds) && a.thirds.SequenceEqual(b.thirds);
                same = same && book.BestPlace(i) == back.BestPlace(i) && book.SeasonsDone(i) == back.SeasonsDone(i);
            }
            // a machine with decimal commas reads it the same (it is all whole numbers, but the point of the check is that nothing depends on the culture)
            var saved = CultureInfo.CurrentCulture;
            string commas;
            try { CultureInfo.CurrentCulture = new CultureInfo("it-IT"); commas = CupBook.Parse(text).Serialize(); }
            finally { CultureInfo.CurrentCulture = saved; }
            bool culture = commas == text;

            // damaged text: nothing throws, nothing out of range gets in, what is good stays
            string good = text.Split('|').FirstOrDefault(e => e.StartsWith("csup=")) ?? "";
            string[] damaged =
            {
                null, "", "garbage", "c125", "c125=", "c125=1,2", "=1,0,0,0,0;0,0", "zzz=1,0,0,0,0;0,0,0,0,0,0,0,0,0,0,0,0;0,0,0,0,0,0,0,0,0,0,0,0;0,0,0,0,0,0,0,0,0,0,0,0;0,0,0,0,0,0,0,0,0,0,0,0",
                "c125=1,9,0,0,0;1,2,3", "c125=3,0,0,0,0;1,2,3", "c125=1,0,0,13,0;1,2,3", "c125=1,2,0,0,0;1,2,3", "c125=1,2,0,0,0;a,b,c,d,e,f,g,h,i,j,k,l;0;0;0",
                "c125=1,2,0,0,0;-5,0,0,0,0,0,0,0,0,0,0,0;0,0,0,0,0,0,0,0,0,0,0,0;0,0,0,0,0,0,0,0,0,0,0,0;0,0,0,0,0,0,0,0,0,0,0,0",
                "c125=1,2,0,0,0;99999999999999999999,0,0,0,0,0,0,0,0,0,0,0;0,0,0,0,0,0,0,0,0,0,0,0;0,0,0,0,0,0,0,0,0,0,0,0;0,0,0,0,0,0,0,0,0,0,0,0",
                "|||", ";;;", "c125=1,2,0,0,0;1,2,3;4;5;6|csup=1,2,3", new string('x', 5000), "c125=1,2,0,0,0" + new string(';', 300),
            };
            var bad = new StringBuilder();
            foreach (var d in damaged)
            {
                try
                {
                    var b = CupBook.Parse(d);
                    for (int i = 0; i < Cups.Count; i++)
                    {
                        var s = b.Season(i);
                        if (s == null) continue;
                        if (s.rank < 0 || s.rank > 2 || s.round < 0 || s.round > s.cup.Rounds || s.freeSkin < 0 || s.freeSkin > 31 || b.BestPlace(i) < 0 || b.BestPlace(i) > Cups.Riders || b.SeasonsDone(i) < 0
                            || s.points.Concat(s.wins).Concat(s.seconds).Concat(s.thirds).Any(v => v < 0 || v > 9999)) bad.Append("[out of range for '" + (d ?? "null").Substring(0, Math.Min(20, (d ?? "null").Length)) + "'] ");
                    }
                }
                catch (Exception e) { bad.Append("[" + e.GetType().Name + " for '" + (d ?? "null").Substring(0, Math.Min(20, (d ?? "null").Length)) + "'] "); }
            }
            var mixed = CupBook.Parse("garbage|" + good + "|c125=zzz");
            bool kept = good.Length > 0 && mixed.Season(1) != null && mixed.Season(1).round == book.Season(1).round && mixed.Season(1).points.SequenceEqual(book.Season(1).points) && mixed.Season(0) == null;
            // damaged numbers on a good header: a blank season, but the trophies stay
            var blank = CupBook.Parse("csup=1,2,3,1,4;1,2");
            bool trophies = blank.Season(1) != null && blank.Season(1).round == 0 && blank.Season(1).points.All(v => v == 0) && blank.BestPlace(1) == 1 && blank.SeasonsDone(1) == 4;
            Check("CUP_BOOK_ROUND_TRIPS_AND_SURVIVES_DAMAGE", same && culture && bad.Length == 0 && kept && trophies,
                "'" + (text.Length > 70 ? text.Substring(0, 70) + "...' (" + text.Length + " characters)" : text + "'") + " comes back the same " + same + ", on a machine with decimal commas " + culture + "; " + damaged.Length + " damaged texts leave nothing out of range " + (bad.Length == 0 ? "true" : bad.ToString()) + ", the good entry among bad ones stays " + kept + ", damaged numbers keep the trophies " + trophies,
                "ASCII text that comes back as it went, and survives anything");
        }

        // ---- a season ridden by the computer on the real circuits: a quicker rider scores more
        {
            var cup = Cups.All[1];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var field = CupField.Make(cup, 1, 7, CupNames, 12);
            var strong = new CupProgress(cup); var weak = new CupProgress(cup);
            int unfinished = 0; string detail = "";
            const int Rounds = 2;
            for (int round = 0; round < Rounds; round++)
            {
                foreach (var pair in new[] { Tuple.Create(strong, 0.97f), Tuple.Create(weak, 0.86f) })
                {
                    int[] order; int finishers;
                    RideCupRound(cup, round, field, pair.Item2, 1, out order, out finishers);
                    unfinished += Cups.Riders - finishers;
                    pair.Item1.Record(order, finishers);
                    detail += " [round " + (round + 1) + " pace " + pair.Item2.ToString("0.00") + ": the player was place " + (Array.IndexOf(order, 0) + 1) + " of " + finishers + "]";
                }
            }
            int total = strong.points.Sum();
            bool everyoneScored = unfinished == 0 && total == Rounds * 134 && weak.points.Sum() == Rounds * 134;
            int pStrong = strong.Position(0), pWeak = weak.Position(0);
            bool quickerScoresMore = strong.points[0] > weak.points[0] && pStrong < pWeak && pStrong <= 6 && pWeak >= 8;
            Check("CUP_COMPUTER_RIDERS_FILL_A_SEASON", everyoneScored && quickerScoresMore,
                Rounds + " rounds of the " + cup.name + " (one lap each) with the real field: everybody finished " + everyoneScored + " (" + total + " points handed out each time), the rider at 0.97 of the line has " + strong.points[0] + " points and is in place " + pStrong + ", the one at 0.86 has " + weak.points[0] + " and is in place " + pWeak + ";" + detail + " in " + sw.ElapsedMilliseconds + " ms",
                "everybody scores, the quicker rider has more points and a better place");
        }

        // ---- the race of a cup does not end when the person crosses the line: the whole field is scored (an ordinary race does end there, the computer riders still out are ranked by distance)
        {
            var cup = Cups.All[1];
            var field = CupField.Make(cup, 1, 7, CupNames, 12);
            int stopped = 0, waited = 0; bool personFinished = true; string detail = "";
            for (int round = 0; round < 2; round++)
            {
                bool person;
                int a = RideCupRoundWithPerson(cup, round, field, false, out person);
                int b = RideCupRoundWithPerson(cup, round, field, true, out person);
                stopped += a; waited += b; personFinished &= person;
                detail += " [round " + (round + 1) + ": " + a + " finished when the person had, " + b + " when the race waited]";
            }
            Check("CUP_RACE_WAITS_FOR_THE_WHOLE_FIELD", personFinished && waited >= 2 * Cups.Riders - 1 && stopped < waited,
                "the person crosses the line first or second and the ordinary rules end the race with " + stopped + " riders in over two rounds; waiting for the field " + waited + " of " + 2 * Cups.Riders + " cross it;" + detail,
                "at least all but one rider in, and more than with the ordinary rules");
        }
    }

    /// <summary>A round of a cup with a person in it (a computer driver stands for the person and gives the inputs, as the game does with -pistabot): how many riders had crossed the line when the race was over, and whether the person had.</summary>
    static int RideCupRoundWithPerson(Cup cup, int round, CupRival[] field, bool waitForField, out bool personFinished)
    {
        var circuit = Circuits.Generate(cup.CircuitSeed(round));
        var race = new Race(circuit, 1, cup.seed + round) { waitForField = waitForField };
        int bike = Cups.BikesOf(cup)[0];
        var me = race.AddHuman("person", bike, 0, 7001 + round);
        var rivals = new List<Racer>();
        for (int i = 0; i < field.Length; i++) rivals.Add(race.AddAi(field[i].name, field[i].bike, field[i].skin, cup.seed * 31 + round * 17 + i * 5 + 3, field[i].pace, field[i].mistakes));
        var order = new List<Racer>(rivals);
        order.Insert(rivals.Count / 2, me);
        race.PlaceAll(order);
        var driver = new BikeDriver(me.bike, circuit, race.LineFor(bike), 7002 + round) { pace = 0.99f, mistakes = 0f };
        driver.Reset();
        race.StartCountdown();
        float t = 0f;
        while (race.phase != RacePhase.Over && t < 900f)
        {
            if (race.phase == RacePhase.Racing) me.input = driver.Drive(RaceDt);
            race.Step(RaceDt); t += RaceDt;
        }
        int n = 0;
        foreach (var r in race.racers) if (r.finished) n++;
        personFinished = me.finished;
        return n;
    }

    /// <summary>One round of a cup by the computer: the field of the cup and one more rider (the player's place, number 0) at a given pace, on the circuit of the round.</summary>
    static void RideCupRound(Cup cup, int round, CupRival[] field, float playerPace, int laps, out int[] order, out int finishers)
    {
        var circuit = Circuits.Generate(cup.CircuitSeed(round));
        var race = new Race(circuit, laps, cup.seed + round);
        var riders = new List<Racer>();
        riders.Add(race.AddAi("player", Cups.BikesOf(cup)[0], 0, 7001 + round, playerPace, 0.04f));
        for (int i = 0; i < field.Length; i++) riders.Add(race.AddAi(field[i].name, field[i].bike, field[i].skin, cup.seed * 31 + round * 17 + i * 5 + 3, field[i].pace, field[i].mistakes));
        race.PlaceAll(riders);
        race.StartCountdown();
        float t = 0f;
        while (race.phase != RacePhase.Over && t < 900f) { race.Step(RaceDt); t += RaceDt; }
        var list = new List<int>();
        finishers = 0;
        foreach (var r in race.standings) { list.Add(riders.IndexOf(r)); if (r.finished) finishers++; }
        order = list.ToArray();
    }
}
