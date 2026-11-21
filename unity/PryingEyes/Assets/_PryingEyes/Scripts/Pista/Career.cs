using System;
using System.Globalization;
using System.Text;

namespace PryingEyes.Pista
{
    /// <summary>The money of the career, an option of the track mode: what a bike costs and what a race, a licence medal and a cup pay. Pure code, no Unity and no random numbers: the same race pays the same every time.
    /// The prices are a table of the game (not worked out from the figures of the bikes, so a change in the physics does not change what a bike costs); the purse of a race follows what the race is: its laps, how many ride
    /// against the player, how good they are and how big the bike is. The units are credits ("cr").</summary>
    public static class Economy
    {
        /// <summary>What a career begins with: this much money, and the bike below (the Scintilla 125).</summary>
        public const int StartMoney = 3000, StarterBike = 3;

        /// <summary>A race with fewer riders against the player than this is practice and pays nothing.</summary>
        public const int MinRivals = 3;

        /// <summary>A licence (the three tests of A1, A2 or A all have a medal) pays this once.</summary>
        public const int LicenceBonus = 1000;

        /// <summary>The price of a bike of the roster, from its key (so the order of the list does not matter). A bike with no price is a mistake of the table: the laboratory counts them.</summary>
        public static int Price(int bike)
        {
            switch (Bikes.Get(bike).id)
            {
                case "scintilla125": return 3500;
                case "vigile750": return 8000;
                case "argo600": return 8500;
                case "tigre675": return 9500;
                case "corsaro1000": return 10500;
                case "rombo500": return 14000;
                case "argo1000": return 14500;
                case "duca1200": return 15500;
                case "gigante1300": return 18500;
                default: return 0;
            }
        }

        /// <summary>How much the level of the rivals counts in a purse: 0 beginner, 1 expert, 2 champion.</summary>
        public static double RankFactor(int rank) { return rank <= 0 ? 0.8 : rank == 1 ? 1.0 : 1.3; }

        /// <summary>The share of the purse of a place (1 is the winner); nothing below the twelfth.</summary>
        public static readonly double[] Shares = { 1.0, 0.60, 0.40, 0.28, 0.20, 0.14, 0.10, 0.07, 0.05, 0.04, 0.03, 0.02 };

        public static double ShareOf(int place) { return place >= 1 && place <= Shares.Length ? Shares[place - 1] : 0.0; }

        /// <summary>The whole purse of a race, for the winner: 60 credits a lap for each rider's place in the field, a bigger bike class and a better level pay more. Whole tens.</summary>
        public static int Purse(int laps, int rivals, int bike, int rank)
        {
            double p = 60.0 * Math.Max(1, laps) * (1.0 + Math.Max(0, rivals) / 6.0) * (1.0 + Price(bike) / 12000.0) * RankFactor(rank);
            return Tens(p);
        }

        /// <summary>What a race pays the player: the purse times the share of the place he took, and nothing if he did not cross the line or the race was practice (too few riders against him).</summary>
        public static int Winnings(int place, bool finished, int laps, int rivals, int bike, int rank)
        {
            if (!finished || rivals < MinRivals) return 0;
            return Tens(Purse(laps, rivals, bike, rank) * ShareOf(place));
        }

        /// <summary>What a medal of a licence test is worth in all (a better medal on the same test pays the difference: see LicencePay).</summary>
        public static int LicenceBounty(Medal medal) { return medal == Medal.Gold ? 1500 : medal == Medal.Silver ? 700 : medal == Medal.Bronze ? 300 : 0; }

        /// <summary>What a lap pays when it takes a test from one medal to another: the difference, never less than nothing.</summary>
        public static int LicencePay(Medal before, Medal after) { return Math.Max(0, LicenceBounty(after) - LicenceBounty(before)); }

        /// <summary>The trophy of a finished season of a cup, on top of what its rounds paid.</summary>
        public static int CupBounty(Medal trophy) { return trophy == Medal.Gold ? 6000 : trophy == Medal.Silver ? 3500 : trophy == Medal.Bronze ? 2000 : 0; }

        /// <summary>The share of the price of a bike that the workshop asks to take it up to each level of Tuning (1, 2, 3): a full tune costs 41 % of the bike.</summary>
        public static readonly double[] TuneShares = { 0.08, 0.13, 0.20 };

        /// <summary>What the workshop asks to take a bike from the level before to this one (1 to 3): a share of its price, in whole tens. Zero for a level that does not exist.</summary>
        public static int TunePrice(int bike, int toLevel) { return toLevel < 1 || toLevel > TuneShares.Length ? 0 : Tens(Price(bike) * TuneShares[toLevel - 1]); }

        static int Tens(double v) { return (int)(Math.Round(v / 10.0, MidpointRounding.AwayFromZero) * 10.0); }

        /// <summary>An amount of money as the game writes it, with a point between the thousands (3.000, 1.234.567).</summary>
        public static string Format(int money)
        {
            string digits = Math.Abs((long)money).ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            for (int i = 0; i < digits.Length; i++)
            {
                if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append('.');
                sb.Append(digits[i]);
            }
            return (money < 0 ? "-" : "") + sb;
        }
    }

    /// <summary>What a player has in the career: the money and the bikes he owns, to and from a short text the preferences keep ("m=3000;o=8;u=000100000": the money, the bikes as a number with one bit for each, and a digit of tuning for each bike). A new book has the starting money
    /// and the starter bike. Reading is forgiving: nothing throws, a mistake leaves the value of a new book, and the starter bike is always owned. Pure code.</summary>
    public sealed class CareerBook
    {
        /// <summary>The most money a book holds (a prize never takes it past this).</summary>
        public const int MaxMoney = 9999999;

        public int Money { get; private set; } = Economy.StartMoney;
        int owned = 1 << Economy.StarterBike;
        readonly int[] levels = new int[Bikes.Count];

        public bool Owns(int bike) { return bike >= 0 && bike < Bikes.Count && (owned & (1 << bike)) != 0; }

        /// <summary>How many bikes are owned.</summary>
        public int OwnedCount
        {
            get { int n = 0; for (int i = 0; i < Bikes.Count; i++) if (Owns(i)) n++; return n; }
        }

        /// <summary>The bike itself if it is owned, else the first one that is (the bike of a choice made before the career, or before a book was lost).</summary>
        public int OwnedOrFirst(int bike)
        {
            if (Owns(bike)) return bike;
            for (int i = 0; i < Bikes.Count; i++) if (Owns(i)) return i;
            return Economy.StarterBike;
        }

        /// <summary>The next owned bike from 'from', one step at a time in the direction (+1 or -1), wrapping round; 'from' itself if it is the only one.</summary>
        public int NextOwned(int from, int direction)
        {
            int step = direction < 0 ? -1 : 1;
            for (int n = 1; n <= Bikes.Count; n++)
            {
                int i = ((from + step * n) % Bikes.Count + Bikes.Count) % Bikes.Count;
                if (Owns(i)) return i;
            }
            return from;
        }

        /// <summary>The first owned bike of a class (a name such as "Superbike"), or -1 when there is none.</summary>
        public int FirstOwnedOf(string klass)
        {
            foreach (int i in Bikes.OfClass(klass)) if (Owns(i)) return i;
            return -1;
        }

        /// <summary>What is still missing to buy a bike (0 when the money is there or the bike is owned).</summary>
        public int Missing(int bike) { return Owns(bike) ? 0 : Math.Max(0, Economy.Price(bike) - Money); }

        /// <summary>Buy a bike: false (and nothing changes) if it is already owned, is not on the list or costs more than there is.</summary>
        public bool Buy(int bike)
        {
            if (bike < 0 || bike >= Bikes.Count || Owns(bike)) return false;
            int price = Economy.Price(bike);
            if (price <= 0 || price > Money) return false;
            Money -= price;
            owned |= 1 << bike;
            levels[bike] = 0;
            return true;
        }

        /// <summary>The level of tuning of a bike (see Tuning): 0 for the bike as it came, and for a bike that is not owned.</summary>
        public int Level(int bike) { return Owns(bike) ? levels[bike] : 0; }

        /// <summary>What is still missing to take an owned bike up one level (0 when the money is there, and for a bike that is not owned or is at the top).</summary>
        public int TuneMissing(int bike)
        {
            int next = Level(bike) + 1;
            if (!Owns(bike) || next > Tuning.MaxLevel) return 0;
            return Math.Max(0, Economy.TunePrice(bike, next) - Money);
        }

        /// <summary>Take an owned bike up one level: false (and nothing changes) if it is not owned, is at the top or costs more than there is.</summary>
        public bool Tune(int bike)
        {
            if (!Owns(bike)) return false;
            int next = levels[bike] + 1;
            if (next > Tuning.MaxLevel) return false;
            int price = Economy.TunePrice(bike, next);
            if (price <= 0 || price > Money) return false;
            Money -= price;
            levels[bike] = next;
            return true;
        }

        /// <summary>A prize arrives (a zero or a negative amount does nothing). The money stops at MaxMoney.</summary>
        public void Pay(int amount)
        {
            if (amount <= 0) return;
            Money = (int)Math.Min((long)MaxMoney, (long)Money + amount);
        }

        public string Serialize()
        {
            var tuning = new StringBuilder();
            for (int i = 0; i < levels.Length; i++) tuning.Append((char)('0' + levels[i]));
            return "m=" + Money.ToString(CultureInfo.InvariantCulture) + ";o=" + owned.ToString(CultureInfo.InvariantCulture) + ";u=" + tuning;
        }

        public static CareerBook Parse(string text)
        {
            var book = new CareerBook();
            if (string.IsNullOrEmpty(text)) return book;
            int valid = (1 << Bikes.Count) - 1;
            foreach (var part in text.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq).Trim();
                string raw = part.Substring(eq + 1).Trim();
                if (key == "u")          // a digit of tuning for each bike; anything else is a bike as it came
                {
                    for (int i = 0; i < book.levels.Length && i < raw.Length; i++) book.levels[i] = raw[i] >= '0' && raw[i] <= '0' + Tuning.MaxLevel ? raw[i] - '0' : 0;
                    continue;
                }
                int v;
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) continue;
                if (key == "m" && v >= 0 && v <= MaxMoney) book.Money = v;
                else if (key == "o" && v > 0) book.owned = (v & valid) | (1 << Economy.StarterBike);
            }
            for (int i = 0; i < book.levels.Length; i++) if (!book.Owns(i)) book.levels[i] = 0;      // a bike that is not owned has no tuning to bring along when it is bought
            return book;
        }
    }
}
