using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The state of the "moto su pista" mode (Scripts/Pista): whether the current game is a race (or a licence test, Licences.cs), the seed of its circuit, and what the player chose in the setup panel (laps, rivals, how good they are,
    /// which bike, how many rider aids, the hour of the day). The choices are remembered in the preferences between sessions; automated runs never touch the preferences and take their values from the
    /// command line (-pistalaps N, -pistarivals N, -pistabike N, -pistaaids N, -pistastyle N, -pistarank N, -pistahour N, -seed N).</summary>
    public static class PistaMode
    {
        const string K = "PryingEyes.Pista.";

        /// <summary>The game in progress is a race on a circuit.</summary>
        public static bool Active { get; private set; }
        /// <summary>The seed of the circuit of the game in progress.</summary>
        public static int Seed { get; private set; }

        static bool? requested;
        /// <summary>Chosen in the menu for the next new game (remembered between sessions). From the command line: -pista.</summary>
        public static bool Requested
        {
            get
            {
                if (requested == null)
                {
                    bool arg = HasArg("-pista") || HasArg("-pistatest");
                    requested = arg || (!Volatile && SafeInt("Requested", 0) == 1);
                }
                return requested.Value;
            }
            set
            {
                requested = value;
                Save("Requested", value ? 1 : 0);
            }
        }

        // ---- the setup panel
        public static readonly int[] LapChoices = { 1, 2, 3, 5, 8 };
        public static readonly int[] RivalChoices = { 0, 3, 7, 11, 15 };
        public static readonly string[] RankNames = { "Principiante", "Esperto", "Campione" };
        public static readonly string[] AidNames = { "Completi", "Sportivi", "Nessuno" };
        public static readonly string[] HourNames = { "Mattino", "Mezzogiorno", "Pomeriggio" };
        /// <summary>How the rider sits in the corners (RiderStyle): hanging off the inside, balanced, or in line with the bike.</summary>
        public static readonly string[] StyleNames = { "Peso fuori", "Neutro", "Moto in piega" };
        static readonly float[] Hours = { 9.2f, 12.8f, 17.2f };

        // In a cooperative game the host decides laps, bike, aids and hour for everybody: a friend's machine takes them from the roster (Override) instead of from its own choices.
        static int? oLaps, oBike, oAids, oHour, oRivals;

        public static int Laps { get => oLaps ?? ArgOr("-pistalaps", "Laps", 3, 1, 20); set => Save("Laps", Mathf.Clamp(value, 1, 20)); }
        public static int Rivals { get => oRivals ?? ArgOr("-pistarivals", "Rivals", 11, 0, 15); set => Save("Rivals", Mathf.Clamp(value, 0, 15)); }
        /// <summary>The bike of the player. In a career (CareerApplies) it is always one that is owned: a choice made before, or of a bike that is not owned, is read as the first one that is. A licence test, a cooperative game and the like
        /// decide the bike themselves (Override) and ownership does not count there; the host of a cooperative game picks any of the nine for the whole table.</summary>
        public static int Bike
        {
            get
            {
                if (oBike != null) return oBike.Value;
                int b = ArgOr("-pistabike", "Bike", 0, 0, Bikes.Count - 1);
                return CareerApplies ? PistaCareer.Book.OwnedOrFirst(b) : b;
            }
            set => Save("Bike", Mathf.Clamp(value, 0, Bikes.Count - 1));
        }
        /// <summary>0 all the aids (the default: the lean assist keeps the lean at the grip limit, and a key is all or nothing), 1 sporty (no lean assist), 2 none (only ABS, manual gears).</summary>
        public static int Aids { get => oAids ?? ArgOr("-pistaaids", "Aids", 0, 0, 2); set => Save("Aids", Mathf.Clamp(value, 0, 2)); }
        /// <summary>How good the computer riders are: 0 beginner, 1 expert, 2 champion.</summary>
        public static int Rank { get => ArgOr("-pistarank", "Rank", 1, 0, 2); set => Save("Rank", Mathf.Clamp(value, 0, 2)); }
        /// <summary>0 lean body, 1 neutral, 2 lean bike (RiderStyle). It is the rider's own way, not a rule of the race: in a cooperative game everybody keeps theirs.</summary>
        public static int Style { get => ArgOr("-pistastyle", "Style", 1, 0, 2); set => Save("Style", Mathf.Clamp(value, 0, 2)); }
        /// <summary>The named circuit (an index into Catalogue.All) chosen in the menu, or -1 for a random one each time.</summary>
        public static int CircuitIndex { get => ArgOr("-pistacircuit", "Circuit", -1, -1, Catalogue.Count - 1); set => Save("Circuit", Mathf.Clamp(value, -1, Catalogue.Count - 1)); }
        public static int HourChoice { get => oHour ?? ArgOr("-pistahour", "Hour", 1, 0, 2); set => Save("Hour", Mathf.Clamp(value, 0, 2)); }
        public static float Hour => Hours[HourChoice];
        /// <summary>The colours of the player's own bike, chosen in the garage: -1 for the livery the bike comes in, or a skin of Livery (the twelve of the game). The computer riders never take it.</summary>
        public static int Skin { get => ArgOr("-pistaskin", "Skin", -1, -1, Livery.Count - 1); set => Save("Skin", Mathf.Clamp(value, -1, Livery.Count - 1)); }
        /// <summary>The skin the player's bike wears: the one chosen in the garage, or the bike's own.</summary>
        public static int SkinFor(int bike) { int s = Skin; return s >= 0 ? s : Bikes.Get(bike).skin; }
        /// <summary>The leathers and the helmet of the player's own rider, chosen in the garage: -1 for the ones of the bike's livery, or those of one of the twelve liveries. Only the player's own rider wears it (a friend in a
        /// cooperative game sees the colours of the bike).</summary>
        public static int Outfit { get => ArgOr("-pistaoutfit", "Outfit", -1, -1, Livery.Count - 1); set => Save("Outfit", Mathf.Clamp(value, -1, Livery.Count - 1)); }
        /// <summary>The livery the player's own bike and rider wear: the bike's, with the leathers and the helmet of the outfit chosen.</summary>
        public static Livery RiderLivery(int skin) { var bike = Livery.For(skin); int o = Outfit; return o < 0 ? bike : Livery.WithRider(bike, o); }

        // ---- the career (Career.cs): an option of the garage, off by default, with its money and bikes in PistaCareer
        /// <summary>The career is on: a free race and a cup are ridden on a bike the player owns, races, licence medals and cups pay, and the garage sells the other bikes. Off, every bike is there for the taking and nothing is paid.
        /// -pistacareer N in the automated runs.</summary>
        public static bool CareerOn { get => ArgOr("-pistacareer", "Career", 0, 0, 1) == 1; set => Save("Career", value ? 1 : 0); }

        /// <summary>The setup panel is the one of a cooperative game about to be hosted (the menu sets it together with modeForCoop). There the host picks the bike of the whole table, so the career does not limit the choice.</summary>
        public static bool ForCoop { get; set; }

        /// <summary>The rules of the career hold now: it is on, and this is neither a cooperative game nor the panel that sets one up (a co-op race pays nothing and the host's bike is for the whole table, owned or not).</summary>
        public static bool CareerApplies => CareerOn && !ForCoop && !Coop.Active;

        /// <summary>The bike a step (+1, -1) of the "Moto" row leads to: the next one of the roster, or in a career the next one that is owned.</summary>
        public static int StepBike(int from, int direction)
        {
            if (CareerApplies) return PistaCareer.Book.NextOwned(from, direction);
            return ((from + (direction < 0 ? -1 : 1)) % Bikes.Count + Bikes.Count) % Bikes.Count;
        }

        /// <summary>The bike a cup is ridden on: the one of the setup panel if it is of the class, else the first of the class (in a career the first of the class that is owned, or -1 when the player owns none: the cup cannot start).</summary>
        public static int CupBike(Cup cup)
        {
            int mine = Bike;
            if (Cups.Allows(cup, mine)) return mine;
            return CareerApplies ? PistaCareer.Book.FirstOwnedOf(cup.klass) : Cups.BikesOf(cup)[0];
        }

        /// <summary>A cup can be ridden: always, except in a career without a bike of its class.</summary>
        public static bool CupAvailable(Cup cup) { return CupBike(cup) >= 0; }

        /// <summary>A friend's machine: the rules of the host's race (laps, bike, aids, hour) for as long as this game lasts.</summary>
        public static void Override(int laps, int bike, int aids, int hour)
        {
            oLaps = Mathf.Clamp(laps, 1, 20); oBike = Mathf.Clamp(bike, 0, Bikes.Count - 1); oAids = Mathf.Clamp(aids, 0, 2); oHour = Mathf.Clamp(hour, 0, 2);
        }

        /// <summary>The licence test being taken (an index into Licences.All), or -1 for an ordinary race. Chosen in the licence panel of the menu for one game; a cooperative game has none.</summary>
        public static int Licence { get; private set; } = -1;

        /// <summary>A licence test is about to start: its bike, aids, hour, laps and (no) rivals take the place of the player's choices until the game is over (End), and its circuit is the one raced.</summary>
        public static void BeginLicence(int index)
        {
            index = Mathf.Clamp(index, 0, Licences.Count - 1);
            var t = Licences.All[index];
            Licence = index; Cup = -1;
            Override(Licences.Laps, t.bike, t.aids, t.hour);
            oRivals = Licences.Rivals;
            Chosen = t.Seed;
        }

        /// <summary>The cup of the championship being raced (an index into Cups.All), or -1 for an ordinary race. Chosen in the championship panel of the menu for one game; a cooperative game has none.</summary>
        public static int Cup { get; private set; } = -1;

        /// <summary>A round of a cup is about to start: its laps (-pistacuplaps N in the automated runs, or 'laps' when a test asks for fewer), a bike of its class (the one of the setup panel if it is of the class, else the
        /// first of the class), the eleven rivals of the season and the circuit of the round take the place of the player's choices until the game is over (End). The aids and the hour are the player's. A cup with no season,
        /// or whose season is over, begins a new one here, at the level and in the colours of now (the trophies of the old ones stay in the book).</summary>
        public static void BeginCup(int cup, int laps = 0)
        {
            cup = Mathf.Clamp(cup, 0, Cups.Count - 1);
            var c = Cups.All[cup];
            Cup = cup; Licence = -1;
            int bike = CupBike(c);
            if (bike < 0) bike = Cups.BikesOf(c)[0];                       // the menu does not start a cup the career has no bike for
            var season = PistaCups.Book.Season(cup);
            if (season == null || season.Finished) PistaCups.Begin(cup, Rank, SkinFor(bike));
            int l = laps > 0 ? laps : TryArg("-pistacuplaps", out int v) ? Mathf.Clamp(v, 1, 20) : c.laps;
            Override(l, bike, Aids, HourChoice);
            oRivals = Cups.Rivals;
            Chosen = CupSeed();
        }

        /// <summary>The seed of the circuit of the round to ride next in the cup being raced (the first round when the cup has no season yet).</summary>
        public static int CupSeed()
        {
            if (Cup < 0) return 0;
            var season = PistaCups.Book.Season(Cup);
            return Cups.All[Cup].CircuitSeed(season != null ? season.NextRound : 0);
        }

        /// <summary>A cooperative game on the host's race is on its way (the host's welcome has said so, the race is not built yet): there are no walking friends to draw.</summary>
        public static bool Incoming { get; set; }

        /// <summary>Set the rider aids of a bike model from the chosen level.</summary>
        public static void ApplyAids(BikeModel b, int level)
        {
            b.abs = true; b.tcs = level < 2; b.antiWheelie = level < 2; b.antiStoppie = level < 2;
            b.leanAssist = level == 0; b.autoGears = level < 2; b.autoHang = true; b.autoTuck = level < 2;
            b.style = (RiderStyle)Mathf.Clamp(Style, 0, 2);
        }

        /// <summary>A race begins: the seed of its circuit.</summary>
        public static void Begin(int seed) { Active = true; Seed = seed; }

        /// <summary>The race is over (menu, new scene).</summary>
        public static void End() { Active = false; Incoming = false; oLaps = oBike = oAids = oHour = oRivals = null; Licence = -1; Cup = -1; Chosen = 0; }

        /// <summary>The circuit picked in the setup panel for the next race (0 = none: a random one).</summary>
        public static int Chosen;

        /// <summary>The seed of a new circuit: -seed in the automated runs (only for the first one of the game: 'first'), the one picked in the menu, random otherwise.</summary>
        public static int NewSeed(bool first = true)
        {
            if (Licence >= 0) return Licences.All[Licence].Seed;          // a licence test is always on its own circuit, again and again
            if (Cup >= 0) return CupSeed();                                // a cup goes round its calendar: the circuit of the next round
            if (first)
            {
                var a = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-seed" && int.TryParse(a[i + 1], out int v) && v != 0) return v;
            }
            if (Chosen != 0) { int c = Chosen; Chosen = 0; return c; }
            // with a named circuit chosen the next one is the next on the list; otherwise any circuit
            int ci = CircuitIndex;
            if (ci >= 0)
            {
                if (!first) { ci = (ci + 1) % Catalogue.Count; CircuitIndex = ci; }
                return Catalogue.All[ci].seed;
            }
            return Random.Range(1, 1 << 28);
        }

        // ---- command line and preferences
        public static bool HasArg(string name) { return System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0; }

        public static bool TryArg(string name, out int value)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name && int.TryParse(a[i + 1], out value)) return true;
            value = 0;
            return false;
        }

        /// <summary>The automated runs (and -pistamenutest, which needs the menu and so cannot be -autotest) neither read nor write the preferences of the player: a value comes from the command line,
        /// from what the run itself has set (kept in memory) or from the default.</summary>
        public static bool Volatile => GameManager.autoTest || HasArg("-pistamenutest");
        static readonly System.Collections.Generic.Dictionary<string, int> memory = new System.Collections.Generic.Dictionary<string, int>();

        static int ArgOr(string arg, string key, int def, int lo, int hi)
        {
            if (TryArg(arg, out int v)) return Mathf.Clamp(v, lo, hi);
            if (Volatile) return memory.TryGetValue(key, out int m) ? Mathf.Clamp(m, lo, hi) : def;
            return Mathf.Clamp(SafeInt(key, def), lo, hi);
        }

        static int SafeInt(string key, int def) { try { return PlayerPrefs.GetInt(K + key, def); } catch { return def; } }

        static void Save(string key, int value)
        {
            if (Volatile) { memory[key] = value; return; }
            try { PlayerPrefs.SetInt(K + key, value); PlayerPrefs.Save(); } catch { /* the preferences are not needed to play */ }
        }
    }
}
