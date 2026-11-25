using System;
using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>The circuits of this game: the generator makes the road from a seed and the game gives it a name, drawn from the generator's own state after the last draw of the circuit, so a seed gives the same road
    /// and the same name on every machine (a record and a ghost are kept under it). Every circuit whose name a player can read is made here.</summary>
    public static class Circuits
    {
        static readonly string[] Kind = { "Circuito", "Autodromo", "Pista", "Anello" };
        static readonly string[] Place =
        {
            "di Monte Corvo", "della Rocca", "del Lago Nero", "di Valle Alta", "dei Cipressi", "di San Gaudenzio", "delle Fornaci", "del Faro",
            "di Poggio Aspro", "della Torre", "del Pozzo", "di Costa Rossa", "dei Tre Ponti", "della Gola", "di Montesole", "del Vento"
        };

        /// <summary>The name of a circuit, from the state of the generator after its last draw.</summary>
        public static string Name(Rng rng) { return Kind[rng.Int(Kind.Length)] + " " + Place[rng.Int(Place.Length)]; }

        public static Circuit Generate(int seed) { return CircuitGen.Generate(seed, Name); }

        public static Circuit Generate(int seed, out int attempts) { return CircuitGen.Generate(seed, out attempts, Name); }
    }
}
