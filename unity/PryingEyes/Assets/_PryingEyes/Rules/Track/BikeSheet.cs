using System;
using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>The seven bars of the garage, each 0..1 on a fixed scale (not against the other bikes, so a new bike in the roster does not move the old ones).</summary>
    public enum BikeBar { Power = 0, Torque = 1, Speed = 2, Pickup = 3, Brakes = 4, Lightness = 5, Handling = 6 }

    public static class BikeSheet
    {
        public const int BarCount = 7;
        public static readonly string[] BikeBarNames = { "Potenza", "Coppia", "Velocita'", "Ripresa", "Frenata", "Leggerezza", "Maneggevolezza" };

        /// <summary>What the garage says about each bike, in a sentence or two (no figures in it: the bars and the numbers carry those, and they change when the bike does).</summary>
        static readonly string[] Blurbs =
        {
            "La superbike di riferimento: quattro cilindri che urlano agli alti giri, lunga e stabile, impenna nelle prime due marce se si apre il gas.",
            "Leggera e pronta nei cambi di direzione, con meta' della spinta della sorella maggiore: si vince in curva, non sul rettilineo.",
            "Una naked da strada, senza carena: coppia piena ai medi, facile da guidare, ma l'aria la ferma presto in fondo al rettilineo.",
            "Un due tempi da Gran Premio in piccolo: pesa quanto il pilota e vive di velocita' in curva. La moto con cui si comincia.",
            "Tre cilindri che ululano: leggera e agile, con una coppia piatta che perdona gli errori.",
            "Un bicilindrico a V: un muro di coppia gia' ai bassi giri, un battito al posto dell'urlo. Impenna nelle prime tre marce.",
            "La piu' veloce in rettilineo e la piu' pesante: passo lungo e ingresso in curva pigro, non ama i tornanti.",
            "Un due tempi V4 da Gran Premio: la potenza arriva tutta insieme sopra i 9000 giri e il peso e' quasi niente. La piu' difficile da guidare.",
            "Una naked con il motore di una superbike: molta potenza e molta aria addosso, piu' adatta alle curve che ai rettilinei.",
        };

        /// <summary>The sentence of a bike of the roster.</summary>
        public static string Blurb(int bikeIndex) { return Blurbs[RacingMath.Clamp(bikeIndex, 0, Blurbs.Length - 1)]; }
        public static int BlurbCount { get { return Blurbs.Length; } }

        static readonly BikeFigures?[] cache = new BikeFigures?[Bikes.Count];

        /// <summary>The figures of a bike of the roster (ridden once, then remembered).</summary>
        public static BikeFigures Of(int bikeIndex)
        {
            int i = RacingMath.Clamp(bikeIndex, 0, Bikes.Count - 1);
            if (cache[i].HasValue) return cache[i].Value;
            var f = BikeFigures.Ride(Bikes.Get(i));
            cache[i] = f;
            return f;
        }

        static readonly BikeFigures?[] tunedCache = new BikeFigures?[Bikes.Count * (Tuning.MaxLevel + 1)];

        /// <summary>The figures of a bike of the roster at a level of tuning (ridden once, then remembered); level 0 is the bike as it comes.</summary>
        public static BikeFigures Of(int bikeIndex, int level)
        {
            int i = RacingMath.Clamp(bikeIndex, 0, Bikes.Count - 1), l = RacingMath.Clamp(level, 0, Tuning.MaxLevel);
            if (l == 0) return Of(i);
            int key = i * (Tuning.MaxLevel + 1) + l;
            if (tunedCache[key].HasValue) return tunedCache[key].Value;
            var f = BikeFigures.Ride(Tuning.Of(i, l));
            tunedCache[key] = f;
            return f;
        }

        /// <summary>The handling bar says no number, a word: how the bike turns in (from heavy to very agile).</summary>
        public static string HandlingWord(BikeFigures f)
        {
            float v = Value(f, BikeBar.Handling);
            return v < 0.30f ? "Pesante" : v < 0.50f ? "Stabile" : v < 0.70f ? "Maneggevole" : "Agilissima";
        }

        /// <summary>One bar of the garage, 0..1.</summary>
        public static float Value(BikeFigures f, BikeBar bar)
        {
            switch (bar)
            {
                case BikeBar.Power: return RacingMath.Clamp01(f.hp / 210f);
                case BikeBar.Torque: return RacingMath.Clamp01(f.torque / 170f);
                case BikeBar.Speed: return RacingMath.Clamp01((f.topKmh - 150f) / 170f);
                case BikeBar.Pickup: return RacingMath.Clamp01((4.6f - f.t100) / 1.7f);
                case BikeBar.Brakes: return RacingMath.Clamp01((46f - f.brake100) / 14f);
                case BikeBar.Lightness: return RacingMath.Clamp01((330f - f.kg) / 210f);
                default: return RacingMath.Clamp01(0.7f * f.agility + 0.3f * f.grip);
            }
        }
    }
}
