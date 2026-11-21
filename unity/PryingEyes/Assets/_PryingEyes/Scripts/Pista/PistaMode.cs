using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The state of the "moto su pista" mode (Scripts/Pista): whether the current game is a race, the seed of its circuit, and what the player chose in the setup panel (laps, rivals, how good they are,
    /// which bike, how many rider aids, the hour of the day). The choices are remembered in the preferences between sessions; automated runs never touch the preferences and take their values from the
    /// command line (-pistalaps N, -pistarivals N, -pistabike N, -pistaaids N, -pistarank N, -pistahour N, -seed N).</summary>
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
        static readonly float[] Hours = { 9.2f, 12.8f, 17.2f };

        // In a cooperative game the host decides laps, bike, aids and hour for everybody: a friend's machine takes them from the roster (Override) instead of from its own choices.
        static int? oLaps, oBike, oAids, oHour;

        public static int Laps { get => oLaps ?? ArgOr("-pistalaps", "Laps", 3, 1, 20); set => Save("Laps", Mathf.Clamp(value, 1, 20)); }
        public static int Rivals { get => ArgOr("-pistarivals", "Rivals", 11, 0, 15); set => Save("Rivals", Mathf.Clamp(value, 0, 15)); }
        public static int Bike { get => oBike ?? ArgOr("-pistabike", "Bike", 0, 0, Bikes.Count - 1); set => Save("Bike", Mathf.Clamp(value, 0, Bikes.Count - 1)); }
        /// <summary>0 all the aids, 1 sporty (no lean assist), 2 none (only ABS, manual gears).</summary>
        public static int Aids { get => oAids ?? ArgOr("-pistaaids", "Aids", 1, 0, 2); set => Save("Aids", Mathf.Clamp(value, 0, 2)); }
        /// <summary>How good the computer riders are: 0 beginner, 1 expert, 2 champion.</summary>
        public static int Rank { get => ArgOr("-pistarank", "Rank", 1, 0, 2); set => Save("Rank", Mathf.Clamp(value, 0, 2)); }
        public static int HourChoice { get => oHour ?? ArgOr("-pistahour", "Hour", 1, 0, 2); set => Save("Hour", Mathf.Clamp(value, 0, 2)); }
        public static float Hour => Hours[HourChoice];

        /// <summary>A friend's machine: the rules of the host's race (laps, bike, aids, hour) for as long as this game lasts.</summary>
        public static void Override(int laps, int bike, int aids, int hour)
        {
            oLaps = Mathf.Clamp(laps, 1, 20); oBike = Mathf.Clamp(bike, 0, Bikes.Count - 1); oAids = Mathf.Clamp(aids, 0, 2); oHour = Mathf.Clamp(hour, 0, 2);
        }

        /// <summary>A cooperative game on the host's race is on its way (the host's welcome has said so, the race is not built yet): there are no walking friends to draw.</summary>
        public static bool Incoming { get; set; }

        /// <summary>Set the rider aids of a bike model from the chosen level.</summary>
        public static void ApplyAids(BikeModel b, int level)
        {
            b.abs = true; b.tcs = level < 2; b.antiWheelie = level < 2; b.antiStoppie = level < 2;
            b.leanAssist = level == 0; b.autoGears = level < 2; b.autoHang = true; b.autoTuck = level < 2;
        }

        /// <summary>A race begins: the seed of its circuit.</summary>
        public static void Begin(int seed) { Active = true; Seed = seed; }

        /// <summary>The race is over (menu, new scene).</summary>
        public static void End() { Active = false; Incoming = false; oLaps = oBike = oAids = oHour = null; }

        /// <summary>The circuit picked in the setup panel for the next race (0 = none: a random one).</summary>
        public static int Chosen;

        /// <summary>The seed of a new circuit: -seed in the automated runs (only for the first one of the game: 'first'), the one picked in the menu, random otherwise.</summary>
        public static int NewSeed(bool first = true)
        {
            if (first)
            {
                var a = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-seed" && int.TryParse(a[i + 1], out int v) && v != 0) return v;
            }
            if (Chosen != 0) { int c = Chosen; Chosen = 0; return c; }
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
        static bool Volatile => GameManager.autoTest || HasArg("-pistamenutest");
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
