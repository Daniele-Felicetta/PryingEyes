using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The player's book of licence times (Licences.cs) kept in the preferences, as one short text under one key. The automated runs (PistaMode.Volatile) keep theirs in memory only: they neither read
    /// nor write the preferences of the player.</summary>
    public static class PistaLicences
    {
        const string Key = "PryingEyes.Pista.Licences";
        static LicenceBook book;
        static string memory = "";

        /// <summary>The book (read from the preferences the first time it is asked for).</summary>
        public static LicenceBook Book
        {
            get
            {
                if (book == null) book = LicenceBook.Parse(Load());
                return book;
            }
        }

        /// <summary>Keep a lap of a test (an index into Licences.All) if it is the best of the test, and write the book; true when it is a new best.</summary>
        public static bool Submit(int test, float lapTime)
        {
            bool better = Book.Submit(test, lapTime);
            if (better) Save(Book.Serialize());
            return better;
        }

        /// <summary>For the tests: a blank book in memory (the preferences are never touched).</summary>
        public static void ForgetAll()
        {
            book = new LicenceBook();
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
