using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>A round of the championship (Championship.cs) in the race. The riders are the eleven of the cup, the same all season, and the player: the first round is gridded by pace (the player in the middle), the
    /// next ones by the standings (the leader on pole). When the table of results comes up the round counts for the season, once (the replay comes back to the table and must not count it again): the points go in
    /// the book of the player, and four lines under the table say what the round gave, who leads and what comes next. INVIO goes on to the next round on the circuit of the calendar; at the end of the season
    /// there is the trophy and the menu. An ordinary race, a licence test and any cooperative game have none of this.</summary>
    public sealed partial class PistaDirector
    {
        int cupIndex = -1;
        CupProgress cupSeason;
        CupRival[] cupField;
        readonly Dictionary<Racer, int> cupRider = new Dictionary<Racer, int>();
        bool cupRecorded;
        string[] cupLines = new string[0];
        int cupGained;

        /// <summary>For the interface and the tests: this race is a round of a cup, its season, the eleven rivals, whether the round closed the season, the points the player gained and the lines under the table.</summary>
        public bool CupActive { get { return cupIndex >= 0 && cupSeason != null; } }
        public CupProgress CupSeason { get { return cupSeason; } }
        public CupRival[] CupRivals { get { return cupField; } }
        public bool CupSeasonOver { get; private set; }
        public int CupGained { get { return cupGained; } }
        public string CupVerdict { get; private set; } = "";

        /// <summary>The number of the rider (0 the player, 1 to 11 the rivals of the cup in the order of the field) of a racer, or -1 for somebody who is not in the cup.</summary>
        public int CupRiderOf(Racer r) { int i; return r != null && cupRider.TryGetValue(r, out i) ? i : -1; }

        /// <summary>The cup is asked for by the menu and this is not a cooperative game: the riders are the player and the eleven of the cup, and the season is the one in the book (a game started without one, like a test,
        /// gets a fresh season at the level and in the colours of now).</summary>
        void BuildCupRiders()
        {
            var cup = Cups.All[cupIndex];
            var season = PistaCups.Book.Season(cupIndex);
            if (season == null) season = PistaCups.Begin(cupIndex, PistaMode.Rank, PistaMode.SkinFor(PistaMode.Bike));
            cupSeason = season;
            race.waitForField = true;                    // the points go to the whole field: the race does not end when the player crosses the line
            string myName = CupPlayerName();
            int bikeMe = PistaMode.Bike;
            me = race.AddHuman(myName, bikeMe, PistaMode.SkinFor(bikeMe), seed + 5, PistaMode.SpecFor(bikeMe));
            me.netSlot = -1;
            PistaMode.ApplyAids(me.bike, PistaMode.Aids);
            cupRider[me] = 0;

            cupField = CupField.Make(cup, season.rank, season.freeSkin, Names, Livery.Count);
            var ais = new List<Racer>();
            for (int i = 0; i < cupField.Length; i++)
            {
                var f = cupField[i];
                int skin = f.skin == me.skin ? season.freeSkin : f.skin;           // a rival never wears the colours the player has now: he takes the livery nobody has
                var r = race.AddAi(f.name, f.bike, skin, seed * 31 + i * 17 + 3, f.pace, f.mistakes);
                cupRider[r] = i + 1;
                ais.Add(r);
            }

            // the grid: the first round by pace (the quickest of the class in front, the player in the middle), then the standings (the leader on pole)
            var order = new List<Racer>();
            if (!season.Started)
            {
                int quickest = Bikes.Quickest(cup.klass);
                var key = new Dictionary<Racer, float>();
                foreach (var r in ais) key[r] = cupField[cupRider[r] - 1].pace + (r.bikeIndex == quickest ? 0.02f : 0f);
                ais.Sort((a, b) => key[b].CompareTo(key[a]));
                int mySlot = ais.Count / 2, k = 0;
                for (int slot = 0; slot <= ais.Count; slot++) order.Add(slot == mySlot ? me : ais[k++]);
            }
            else
            {
                var byRider = new Dictionary<int, Racer>();
                foreach (var kv in cupRider) byRider[kv.Value] = kv.Key;
                foreach (int rider in season.Order()) order.Add(byRider[rider]);
            }
            race.PlaceAll(order);
        }

        /// <summary>A race is set up: a fresh round, nothing counted yet and no lines under the table.</summary>
        void CupBegin()
        {
            cupRecorded = false; cupLines = new string[0]; cupGained = 0; CupSeasonOver = false; CupVerdict = "";
            if (hud != null) hud.SetCup(null);
        }

        /// <summary>The player has crossed the line but the race goes on (the whole field is scored): the table comes when the last rider is in.</summary>
        void CupFinished() { hud.SetHints("Gli altri stanno ancora arrivando", 40f); }

        /// <summary>The banner of the start (it replaces the one of the circuit): the cup in the big line, short enough to stay clear of the minimap, and the round, the circuit, the laps and the bike under it.</summary>
        void CupBanner()
        {
            var cup = Cups.All[cupIndex];
            hud.Banner(cup.name.ToUpperInvariant(),
                "GARA " + (cupSeason.round + 1) + " DI " + cup.Rounds + "   -   " + circuit.name + "   -   " + race.laps + (race.laps == 1 ? " giro" : " giri") + "   -   " + MyBikeName(), 4.2f, new Color(1f, 0.74f, 0.18f));
        }

        string CupRiderName(int rider) { return rider == 0 ? me.name : cupField[rider - 1].name; }

        /// <summary>The name of the player in a cup (the character in use, or "Tu" for a game without one): the table of the round and the menu say it.</summary>
        internal static string CupPlayerName()
        {
            var mine = Characters.Progress.Mine;
            return mine != null && !string.IsNullOrEmpty(mine.name) ? mine.name : "Tu";
        }

        internal static string TrophyName(Medal trophy)
        {
            return trophy == Medal.Gold ? "COPPA D'ORO" : trophy == Medal.Silver ? "COPPA D'ARGENTO" : trophy == Medal.Bronze ? "COPPA DI BRONZO" : "";
        }

        /// <summary>The table is up: the round counts for the season (once), and the four lines under the table say what it gave. Called again when the replay comes back to the table: it only shows the lines again.</summary>
        void CupResults()
        {
            if (!CupActive) { hud.SetCup(null); return; }
            var cup = Cups.All[cupIndex];
            if (!cupRecorded)
            {
                cupRecorded = true;
                var order = new List<int>();
                int finishers = 0;
                foreach (var r in race.standings)
                {
                    int rider = CupRiderOf(r);
                    if (rider < 0) continue;
                    order.Add(rider);
                    if (r.finished) finishers++;
                }
                int before = cupSeason.points[0], done = cupSeason.round;
                CupSeasonOver = PistaCups.Record(cupIndex, order, finishers);
                cupGained = cupSeason.points[0] - before;
                cupLines = CupLines(cup, done);
                CupVerdict = string.Join(" | ", cupLines);
            }
            hud.SetCup(cupLines);
        }

        string[] CupLines(Cup cup, int done)
        {
            var lines = new string[4];
            int place = cupSeason.Position(0);
            lines[0] = cup.name.ToUpperInvariant() + "  -  GARA " + (done + 1) + " DI " + cup.Rounds;
            string standing = "in classifica  " + place + "°  con  " + cupSeason.points[0] + " punti";
            lines[1] = (me.finished ? me.position + "°:  " + (cupGained > 0 ? "+" + cupGained + " punti" : "nessun punto") : "Non hai tagliato il traguardo: nessun punto") + "      -      " + standing;
            var order = cupSeason.Order();
            var sb = new StringBuilder("Classifica:  ");
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) sb.Append("   ");
                sb.Append(i + 1).Append("  ").Append(CupRiderName(order[i])).Append(' ').Append(cupSeason.points[order[i]]);
            }
            if (place > 3) sb.Append("   ...   ").Append(place).Append("  ").Append(me.name).Append(' ').Append(cupSeason.points[0]);
            lines[2] = sb.ToString();
            if (CupSeasonOver)
            {
                var trophy = cupSeason.Trophy();
                lines[3] = "STAGIONE FINITA  -  " + place + "° in classifica" + (trophy != Medal.None ? "  -  " + TrophyName(trophy) : "");
            }
            else lines[3] = "Prossima gara:  " + CircuitGen.Generate(cup.CircuitSeed(cupSeason.round)).name;           // the key is in the hint at the foot of the table
            return lines;
        }

        /// <summary>What the foot of the table says to do: the next round, or only the replay and the menu when the season is over.</summary>
        string CupHint()
        {
            bool more = cupSeason != null && !cupSeason.Finished;
            if (more) return input.pad ? "A  prossima gara   -   Y  replay   -   START  menu" : "INVIO  prossima gara   -   T  replay   -   ESC  menu";
            return input.pad ? "Y  replay   -   START  menu" : "T  replay   -   ESC  menu";
        }

        /// <summary>INVIO on the table of a round: the next round of the cup, on the circuit of the calendar, with the same riders. False when there is none (the season is over, or the round is not counted yet).</summary>
        public bool CupNext()
        {
            if (!CupActive || !cupRecorded || cupSeason.Finished || !CanRestart()) return false;
            seed = PistaMode.NewSeed(false);
            StopAllCoroutines();
            StartCoroutine(SetupRace(seed, true));
            return true;
        }
    }
}
