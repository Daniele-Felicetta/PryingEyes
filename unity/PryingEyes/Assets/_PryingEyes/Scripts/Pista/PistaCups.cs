using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The player's championship (Championship.cs) kept in the preferences, as one short text under one key: the season of every cup and the trophies. The automated runs (PistaMode.Volatile) keep theirs
    /// in memory only: they neither read nor write the preferences of the player.</summary>
    public static class PistaCups
    {
        const string Key = "PryingEyes.Pista.Cups";
        static CupBook book;
        static string memory = "";

        /// <summary>The book (read from the preferences the first time it is asked for).</summary>
        public static CupBook Book
        {
            get
            {
                if (book == null) book = CupBook.Parse(Load());
                return book;
            }
        }

        /// <summary>A new season of a cup, written down: the level of the rivals is the one of the setup panel now, and 'freeSkin' the livery of the player's bike (no rival wears it).</summary>
        public static CupProgress Begin(int cup, int rank, int freeSkin)
        {
            var season = Book.Begin(cup, rank, freeSkin);
            Save(Book.Serialize());
            return season;
        }

        /// <summary>A round is over: the places of the riders (0 is the player) and how many crossed the line. True when it closed the season. Written down at once.</summary>
        public static bool Record(int cup, IList<int> order, int finishers)
        {
            bool closed = Book.Record(cup, order, finishers);
            Save(Book.Serialize());
            return closed;
        }

        /// <summary>For the tests: a blank book in memory (the preferences are never touched).</summary>
        public static void ForgetAll()
        {
            book = new CupBook();
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
