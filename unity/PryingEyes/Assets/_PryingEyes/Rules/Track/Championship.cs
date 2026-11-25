using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PryingEyes.Pista
{
    /// <summary>One cup of the championship: a class of bikes (the player rides any bike of it), four rounds on named circuits of the Catalogue, three laps each, and a field of eleven computer riders that is
    /// the same all season (CupField). A season is what the player rides one round after the other; the points of every rider (the player and the eleven) are added up round by round. Pure code, no Unity.</summary>
    public sealed class Cup
    {
        public readonly string id, name, klass, tagline;
        public readonly int laps, seed;
        /// <summary>The circuit of every round: an index into Catalogue.All.</summary>
        public readonly int[] circuits;

        public Cup(string id, string name, string klass, string tagline, int laps, int seed, params int[] circuits)
        {
            this.id = id; this.name = name; this.klass = klass; this.tagline = tagline; this.laps = laps; this.seed = seed; this.circuits = circuits;
        }

        public int Rounds { get { return circuits.Length; } }

        /// <summary>The seed of the circuit of a round (a round outside the calendar is taken as the nearest one).</summary>
        public int CircuitSeed(int round) { return Catalogue.All[circuits[Math.Max(0, Math.Min(circuits.Length - 1, round))]].seed; }
    }

    /// <summary>The cups of the championship, the table of points and who may ride what.</summary>
    public static class Cups
    {
        /// <summary>The computer riders of a season and the field with the player.</summary>
        public const int Rivals = 11, Riders = Rivals + 1;

        /// <summary>The points of the places from first to fifteenth; whoever is lower, or does not cross the line, scores nothing.</summary>
        public static readonly int[] PointsTable = { 25, 20, 16, 13, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };

        public static readonly Cup[] All =
        {
            new Cup("c125", "Coppa 125", "125 GP", "La piccola due tempi: si vive di velocita' in curva, e i quattro circuiti sono quelli di chi comincia.", 3, 1251, 0, 3, 4, 7),
            new Cup("csup", "Coppa Supersport", "Supersport", "Argo 600 o Tigre 675: dal circuito veloce a quello che non da' respiro.", 3, 6001, 1, 5, 8, 9),
            new Cup("csbk", "Coppa Superbike", "Superbike", "Argo 1000 o Duca 1200: dal rettilineo di 850 metri alla pista di punta.", 3, 10001, 2, 6, 10, 11),
        };

        public static int Count { get { return All.Length; } }

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return i;
            return -1;
        }

        /// <summary>The points of a place (1 is the winner); nothing for a rider who did not cross the line or is below the table.</summary>
        public static int PointsFor(int place, bool crossedTheLine)
        {
            return crossedTheLine && place >= 1 && place <= PointsTable.Length ? PointsTable[place - 1] : 0;
        }

        /// <summary>The bikes of the class of a cup, in the order of the menu: the player may ride any of them.</summary>
        public static int[] BikesOf(Cup cup) { return Bikes.OfClass(cup.klass); }

        public static bool Allows(Cup cup, int bike) { return bike >= 0 && bike < Bikes.Count && Bikes.Get(bike).klass == cup.klass; }
    }

    /// <summary>A computer rider of a cup: who he is (name, bike, colours) and how he rides (pace against the racing line, how often he overdoes a corner). All of it stays the same all season.</summary>
    public sealed class CupRival
    {
        public string name;
        public int bike, skin;
        public float pace, mistakes;
    }

    /// <summary>The eleven rivals of a cup. Their names, bikes and colours come from the cup alone; their pace is the level of the season (beginner, expert, champion) plus a fixed share of the spread, so a rival that is
    /// quick in the first round is quick in the last, which is what makes a standings table mean something. The generator is our own (xorshift): the same cup gives the same field on every machine and after every update of the engine.</summary>
    public static class CupField
    {
        public static float BasePace(int rank) { return rank <= 0 ? 0.885f : rank == 1 ? 0.935f : 0.968f; }
        public static float Spread(int rank) { return rank <= 0 ? 0.06f : rank == 1 ? 0.045f : 0.027f; }
        public static float Mistakes(int rank) { return rank <= 0 ? 0.28f : rank == 1 ? 0.12f : 0.04f; }

        /// <summary>The field of a cup at a level. 'freeSkin' is the livery no rival wears (the player's, when the season began; a rival who has the colours of the player's bike later is given it back by the game).
        /// 'names' are the names of the riders of the game, 'skinCount' the liveries it has.</summary>
        public static CupRival[] Make(Cup cup, int rank, int freeSkin, IList<string> names, int skinCount)
        {
            var rng = new CupRng(cup.seed);
            var pool = new List<string>(names);
            Shuffle(pool, rng);
            var skins = new List<int>();
            for (int i = 0; i < skinCount; i++) if (i != freeSkin) skins.Add(i);
            Shuffle(skins, rng);
            int[] mates = Bikes.OfClass(cup.klass);
            var field = new CupRival[Cups.Rivals];
            for (int i = 0; i < field.Length; i++)
            {
                var r = new CupRival();
                r.name = pool[i % pool.Count];
                r.bike = mates.Length > 0 ? mates[rng.Int(mates.Length)] : 0;
                r.skin = skins[i % skins.Count];
                r.pace = BasePace(rank) + Spread(rank) * (float)(rng.Double() * 2.0 - 1.0);
                r.mistakes = Mistakes(rank);
                field[i] = r;
            }
            return field;
        }

        static void Shuffle<T>(IList<T> list, CupRng rng)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = rng.Int(i + 1); T t = list[i]; list[i] = list[j]; list[j] = t; }
        }
    }

    /// <summary>A small generator of our own, so that a field made from a seed does not depend on the runtime.</summary>
    sealed class CupRng
    {
        uint s;

        public CupRng(int seed)
        {
            s = unchecked((uint)seed * 2654435761u + 0x9E3779B9u);
            if (s == 0) s = 0x1234567u;
            for (int i = 0; i < 4; i++) Next();
        }

        public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }

        /// <summary>0 or more, under 1.</summary>
        public double Double() { return (Next() >> 8) / 16777216.0; }

        /// <summary>0 or more, under n.</summary>
        public int Int(int n) { return n <= 1 ? 0 : (int)(Next() % (uint)n); }
    }

    /// <summary>A season of one cup: the points, wins and second and third places of the twelve riders (0 is the player, 1 to 11 the rivals in the order of the field), and how many rounds have been ridden.</summary>
    public sealed class CupProgress
    {
        public readonly Cup cup;
        /// <summary>The level of the rivals (fixed when the season began) and the livery of the field that no rival wears.</summary>
        public int rank = 1, freeSkin;
        /// <summary>Rounds finished.</summary>
        public int round;
        public readonly int[] points = new int[Cups.Riders], wins = new int[Cups.Riders], seconds = new int[Cups.Riders], thirds = new int[Cups.Riders];

        public CupProgress(Cup cup) { this.cup = cup; }

        public bool Started { get { return round > 0; } }
        public bool Finished { get { return round >= cup.Rounds; } }

        /// <summary>The round to ride next (the last one when the season is over).</summary>
        public int NextRound { get { return Math.Min(round, cup.Rounds - 1); } }

        /// <summary>A round is over. 'order' lists the riders from first to last place; 'finishers' says how many of them, from the front, crossed the line: only those score. A season that is over takes no more rounds.</summary>
        public void Record(IList<int> order, int finishers)
        {
            if (Finished) return;
            for (int p = 0; p < order.Count; p++)
            {
                int r = order[p];
                if (r < 0 || r >= Cups.Riders) continue;
                bool crossed = p < finishers;
                points[r] += Cups.PointsFor(p + 1, crossed);
                if (!crossed) continue;
                if (p == 0) wins[r]++; else if (p == 1) seconds[r]++; else if (p == 2) thirds[r]++;
            }
            round++;
        }

        /// <summary>The riders from first to last in the standings: points, then wins, then second places, then third places, then the order of the field (the player first).</summary>
        public int[] Order()
        {
            var idx = new int[Cups.Riders];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            Array.Sort(idx, (a, b) =>
            {
                int c = points[b].CompareTo(points[a]); if (c != 0) return c;
                c = wins[b].CompareTo(wins[a]); if (c != 0) return c;
                c = seconds[b].CompareTo(seconds[a]); if (c != 0) return c;
                c = thirds[b].CompareTo(thirds[a]); if (c != 0) return c;
                return a.CompareTo(b);
            });
            return idx;
        }

        /// <summary>The place of a rider in the standings (1 is the leader).</summary>
        public int Position(int rider)
        {
            var o = Order();
            for (int i = 0; i < o.Length; i++) if (o[i] == rider) return i + 1;
            return o.Length;
        }

        /// <summary>What the player has earned in this season once it is over: gold for the first place, silver for the second, bronze for the third; nothing before that or lower.</summary>
        public Medal Trophy()
        {
            if (!Finished) return Medal.None;
            int p = Position(0);
            return p == 1 ? Medal.Gold : p == 2 ? Medal.Silver : p == 3 ? Medal.Bronze : Medal.None;
        }
    }

    /// <summary>What a player has done in the championship: the season of every cup (the one being ridden, or the one just finished), the best place a finished season ended in and how many seasons were finished.
    /// To and from a short text the preferences keep ("c125=1,2,3,0,0;38,41,...;..."); reading is forgiving: anything that is not a known cup with believable numbers is left out, and nothing throws.</summary>
    public sealed class CupBook
    {
        readonly CupProgress[] season = new CupProgress[Cups.Count];
        readonly int[] best = new int[Cups.Count], seasons = new int[Cups.Count];

        /// <summary>The season of a cup, or null if it was never begun.</summary>
        public CupProgress Season(int cup) { return cup >= 0 && cup < season.Length ? season[cup] : null; }

        /// <summary>The best place a finished season ended in (0: no finished season yet).</summary>
        public int BestPlace(int cup) { return cup >= 0 && cup < best.Length ? best[cup] : 0; }

        public int SeasonsDone(int cup) { return cup >= 0 && cup < seasons.Length ? seasons[cup] : 0; }

        public Medal BestTrophy(int cup)
        {
            int p = BestPlace(cup);
            return p == 1 ? Medal.Gold : p == 2 ? Medal.Silver : p == 3 ? Medal.Bronze : Medal.None;
        }

        /// <summary>How many cups have a trophy of this level or better.</summary>
        public int Trophies(Medal atLeast)
        {
            int n = 0;
            for (int i = 0; i < best.Length; i++) { Medal m = BestTrophy(i); if (m != Medal.None && m >= atLeast) n++; }
            return n;
        }

        /// <summary>A new season of a cup: the one before (finished or not) is dropped, the trophies stay.</summary>
        public CupProgress Begin(int cup, int rank, int freeSkin)
        {
            var p = new CupProgress(Cups.All[cup]) { rank = Math.Max(0, Math.Min(2, rank)), freeSkin = Math.Max(0, freeSkin) };
            season[cup] = p;
            return p;
        }

        /// <summary>The round just ridden. True when it closed the season, which also keeps the best place and counts the season.</summary>
        public bool Record(int cup, IList<int> order, int finishers)
        {
            var p = Season(cup);
            if (p == null || p.Finished) return false;
            p.Record(order, finishers);
            if (!p.Finished) return false;
            int place = p.Position(0);
            seasons[cup]++;
            if (best[cup] == 0 || place < best[cup]) best[cup] = place;
            return true;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < season.Length; i++)
            {
                var p = season[i];
                if (p == null && best[i] == 0 && seasons[i] == 0) continue;
                if (p == null) p = new CupProgress(Cups.All[i]);
                if (sb.Length > 0) sb.Append('|');
                sb.Append(Cups.All[i].id).Append('=')
                  .Append(p.rank).Append(',').Append(p.round).Append(',').Append(p.freeSkin).Append(',').Append(best[i]).Append(',').Append(seasons[i]);
                Append(sb, p.points); Append(sb, p.wins); Append(sb, p.seconds); Append(sb, p.thirds);
            }
            return sb.ToString();
        }

        static void Append(StringBuilder sb, int[] values)
        {
            sb.Append(';');
            for (int i = 0; i < values.Length; i++) { if (i > 0) sb.Append(','); sb.Append(values[i].ToString(CultureInfo.InvariantCulture)); }
        }

        public static CupBook Parse(string text)
        {
            var book = new CupBook();
            if (string.IsNullOrEmpty(text)) return book;
            foreach (var entry in text.Split('|'))
            {
                int eq = entry.IndexOf('=');
                if (eq <= 0) continue;
                int cup = Cups.IndexOf(entry.Substring(0, eq).Trim());
                if (cup < 0) continue;
                var parts = entry.Substring(eq + 1).Split(';');
                var head = parts[0].Split(',');
                int rank, round, free, bestPlace, done;
                if (head.Length != 5 || !Number(head[0], 0, 2, out rank) || !Number(head[1], 0, Cups.All[cup].Rounds, out round) || !Number(head[2], 0, 31, out free)
                    || !Number(head[3], 0, Cups.Riders, out bestPlace) || !Number(head[4], 0, 9999, out done)) continue;
                var p = new CupProgress(Cups.All[cup]) { rank = rank, freeSkin = free, round = round };
                bool ok = parts.Length == 5 && Fill(parts[1], p.points) && Fill(parts[2], p.wins) && Fill(parts[3], p.seconds) && Fill(parts[4], p.thirds);
                if (!ok) p = new CupProgress(Cups.All[cup]) { rank = rank, freeSkin = free };        // damaged numbers: a blank season, the trophies stay
                book.season[cup] = p; book.best[cup] = bestPlace; book.seasons[cup] = done;
            }
            return book;
        }

        static bool Number(string text, int lo, int hi, out int value)
        {
            return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= lo && value <= hi;
        }

        static bool Fill(string text, int[] into)
        {
            var items = text.Split(',');
            if (items.Length != into.Length) return false;
            var tmp = new int[into.Length];
            for (int i = 0; i < items.Length; i++) if (!Number(items[i], 0, 9999, out tmp[i])) return false;
            Array.Copy(tmp, into, tmp.Length);
            return true;
        }
    }
}
