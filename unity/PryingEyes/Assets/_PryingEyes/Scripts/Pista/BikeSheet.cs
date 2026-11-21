using System;

namespace PryingEyes.Pista
{
    /// <summary>The numbers of one bike as the garage shows them, worked out by riding the real model, not by a formula: the engine on paper (power, weight) and, on a flat clean road with every aid on, the top
    /// speed after a minute at full throttle, the time from a standing start to 100 km/h and the distance to stop from 100 km/h with both brakes. Pure code, deterministic (same bike, same numbers).</summary>
    public struct BikeFigures
    {
        public float hp, torque, kg, topKmh, t100, brake100;
        /// <summary>Handling, 0..1: how quickly the bike rolls into a turn (its roll rate at a crawl) and how tight it can go; and grip, the mean peak friction of its two tires.</summary>
        public float agility, grip;
    }

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
        public static string Blurb(int bikeIndex) { return Blurbs[PM.Clamp(bikeIndex, 0, Blurbs.Length - 1)]; }
        public static int BlurbCount { get { return Blurbs.Length; } }

        static readonly BikeFigures?[] cache = new BikeFigures?[Bikes.Count];

        /// <summary>The figures of a bike of the roster (ridden once, then remembered).</summary>
        public static BikeFigures Of(int bikeIndex)
        {
            int i = PM.Clamp(bikeIndex, 0, Bikes.Count - 1);
            if (cache[i].HasValue) return cache[i].Value;
            var f = Ride(Bikes.Get(i));
            cache[i] = f;
            return f;
        }

        static readonly BikeFigures?[] tunedCache = new BikeFigures?[Bikes.Count * (Tuning.MaxLevel + 1)];

        /// <summary>The figures of a bike of the roster at a level of tuning (ridden once, then remembered); level 0 is the bike as it comes.</summary>
        public static BikeFigures Of(int bikeIndex, int level)
        {
            int i = PM.Clamp(bikeIndex, 0, Bikes.Count - 1), l = PM.Clamp(level, 0, Tuning.MaxLevel);
            if (l == 0) return Of(i);
            int key = i * (Tuning.MaxLevel + 1) + l;
            if (tunedCache[key].HasValue) return tunedCache[key].Value;
            var f = Ride(Tuning.Of(i, l));
            tunedCache[key] = f;
            return f;
        }

        /// <summary>Ride a spec on a flat road: top speed, 0-100 and 100-0.</summary>
        public static BikeFigures Ride(BikeSpec spec)
        {
            const float dt = 1f / 120f;
            var flat = new FlatGround();
            float rpm;
            var f = new BikeFigures { hp = spec.PeakPowerKw(out rpm) * 1.36f, kg = spec.Mass, t100 = -1f };
            var bike = new BikeModel(spec, 7);
            bike.Reset(0f, 0f, 0f, 0f);
            for (float t = 0f; t < 60f; t += dt)
            {
                bike.Step(dt, flat, new BikeInputs { throttle = 1f });
                float kmh = bike.u * 3.6f;
                if (f.t100 < 0f && kmh >= 100f) f.t100 = t;
                if (kmh > f.topKmh) f.topKmh = kmh;
            }
            var stop = new BikeModel(spec, 3);
            stop.Reset(0f, 0f, 0f, 0f);
            float v0 = 100f / 3.6f;
            stop.u = v0; stop.omegaF = stop.omegaR = v0 / spec.wheelRadius; stop.gear = Math.Min(3, spec.GearCount);
            for (float t = 0f; t < 12f; t += dt)
            {
                stop.Step(dt, flat, new BikeInputs { brakeFront = 1f, brakeRear = 0.35f });
                f.brake100 = stop.z;
                if (stop.u < 0.2f) break;
            }
            foreach (float nm in spec.torqueCurve) f.torque = Math.Max(f.torque, nm);
            f.agility = PM.Clamp01((spec.rollRateLow - 1.4f) / 1.2f);
            f.grip = PM.Clamp01((0.5f * (spec.muFront + spec.muRear) - 1.15f) / 0.40f);
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
                case BikeBar.Power: return PM.Clamp01(f.hp / 210f);
                case BikeBar.Torque: return PM.Clamp01(f.torque / 170f);
                case BikeBar.Speed: return PM.Clamp01((f.topKmh - 150f) / 170f);
                case BikeBar.Pickup: return PM.Clamp01((4.6f - f.t100) / 1.7f);
                case BikeBar.Brakes: return PM.Clamp01((46f - f.brake100) / 14f);
                case BikeBar.Lightness: return PM.Clamp01((330f - f.kg) / 210f);
                default: return PM.Clamp01(0.7f * f.agility + 0.3f * f.grip);
            }
        }
    }
}
