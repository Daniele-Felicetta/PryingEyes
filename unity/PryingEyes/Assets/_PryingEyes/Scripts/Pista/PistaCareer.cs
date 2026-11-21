using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The player's career (Career.cs) kept in the preferences, as one short text under one key: the money and the bikes owned. The automated runs (PistaMode.Volatile) keep theirs in memory only: they neither read
    /// nor write the preferences of the player. Whether the career is on at all is PistaMode.CareerOn (an option of the garage, off by default).</summary>
    public static class PistaCareer
    {
        // not "PryingEyes.Pista.Career": that is the key of the on/off switch (PistaMode.CareerOn writes an integer under it) and a preference holds one value, so each would have wiped the other
        const string Key = "PryingEyes.Pista.CareerBook";
        static CareerBook book;
        static string memory = "";

        /// <summary>The book (read from the preferences the first time it is asked for).</summary>
        public static CareerBook Book
        {
            get
            {
                if (book == null) book = CareerBook.Parse(Load());
                return book;
            }
        }

        /// <summary>A prize arrives and is written down at once; the amount that went into the book (the book has a ceiling).</summary>
        public static int Pay(int amount)
        {
            int before = Book.Money;
            Book.Pay(amount);
            if (Book.Money != before) Save(Book.Serialize());
            return Book.Money - before;
        }

        /// <summary>Buy a bike: written down at once when it is bought.</summary>
        public static bool Buy(int bike)
        {
            bool bought = Book.Buy(bike);
            if (bought) Save(Book.Serialize());
            return bought;
        }

        /// <summary>Take an owned bike up one level of tuning (see Tuning): written down at once when it is done.</summary>
        public static bool Tune(int bike)
        {
            bool done = Book.Tune(bike);
            if (done) Save(Book.Serialize());
            return done;
        }

        /// <summary>For the tests: a new career in memory (the preferences are never touched).</summary>
        public static void ForgetAll()
        {
            book = new CareerBook();
            memory = "";
        }

        static string Load()
        {
            if (PistaMode.Volatile) return memory;
            try { return PlayerPrefs.GetString(Key, ""); } catch { return ""; }
        }

        static void Save(string text)
        {
            if (PistaMode.Volatile) { memory = text; return; }
            try { PlayerPrefs.SetString(Key, text); PlayerPrefs.Save(); } catch { /* the preferences are not needed to play */ }
        }
    }
}
