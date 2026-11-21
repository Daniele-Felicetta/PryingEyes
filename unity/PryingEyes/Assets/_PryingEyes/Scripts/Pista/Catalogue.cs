using System;

namespace PryingEyes.Pista
{
    /// <summary>The named circuits of the track mode: seeds picked (with tools/tracklab --scan) for their character, so that a record, a ghost or a licence test means something, because the circuit
    /// is the same every time. The generator still makes every circuit from its seed; this is only the list of the ones worth knowing by name. A circuit that is not on the list ("Casuale" in the menu)
    /// is one of a kind, and nothing about it is remembered.</summary>
    public static class Catalogue
    {
        public struct Entry
        {
            /// <summary>The seed the circuit is made from.</summary>
            public int seed;
            /// <summary>The length the generator must still give (m, rounded): a record is only worth anything on the same circuit, so a change to the generator that moves it must be a decision.</summary>
            public int length;
            /// <summary>A line of the character of the circuit, for the menu.</summary>
            public string tag;

            public Entry(int seed, int length, string tag) { this.seed = seed; this.length = length; this.tag = tag; }
        }

        public static readonly Entry[] All =
        {
            new Entry(4242, 2732, "Scorrevole: il circuito per cominciare"),
            new Entry(22, 2908, "Veloce, con un lungo rettilineo"),
            new Entry(205, 3165, "Velocissimo: oltre 850 m di rettilineo"),
            new Entry(302, 2828, "Saliscendi scorrevole"),
            new Entry(64, 2508, "Corto, veloce e collinare"),
            new Entry(53, 2936, "Tecnico e piatto: tanti tornanti"),
            new Entry(236, 3084, "Il piu' tecnico: quindici tornanti"),
            new Entry(92, 2938, "Tornanti in salita"),
            new Entry(97, 3612, "Misto e lungo"),
            new Entry(291, 3506, "Collinare, senza respiro"),
            new Entry(111, 4055, "Lungo e scorrevole"),
            new Entry(165, 4139, "Il piu' lungo: la pista di punta"),
        };

        public static int Count { get { return All.Length; } }

        /// <summary>The index of the circuit of this seed, or -1 when it is not one of the named ones.</summary>
        public static int IndexOf(int seed)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].seed == seed) return i;
            return -1;
        }

        public static bool Named(int seed) { return IndexOf(seed) >= 0; }
    }
}
