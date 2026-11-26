using System;
using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>The bikes of the track mode: nine machines from a 125 two-stroke that weighs as much as the rider to a 1300 that tops 300 km/h. Their numbers are those of real bikes of the kind (mass,
    /// power, gearing, tires), not the numbers of a game; the rivals of a race ride the class of the player, so a race is between bikes that are a match for each other.</summary>
    public static class Bikes
    {
        public const int Count = 9;

        public static BikeSpec Get(int index)
        {
            switch (RacingMath.Clamp(index, 0, Count - 1))
            {
                case 1: return Supersport();
                case 2: return Roadster();
                case 3: return Gp125();
                case 4: return Triple675();
                case 5: return Twin1200();
                case 6: return Hyper1300();
                case 7: return Gp500();
                case 8: return Naked1000();
                default: return Superbike();
            }
        }

        /// <summary>The indices of the bikes of a class ("Superbike", "Supersport", ...), in the order of the menu.</summary>
        public static int[] OfClass(string klass)
        {
            var found = new System.Collections.Generic.List<int>();
            for (int i = 0; i < Count; i++) if (Get(i).klass == klass) found.Add(i);
            return found.ToArray();
        }

        /// <summary>The one with the most power for its weight in the class of this bike (the rivals' grid puts the quicker bikes in front).</summary>
        public static int Quickest(string klass)
        {
            int best = -1; float bestRatio = -1f;
            foreach (int i in OfClass(klass))
            {
                var s = Get(i); float rpm;
                float ratio = s.PeakPowerKw(out rpm) / s.Mass;
                if (ratio > bestRatio) { bestRatio = ratio; best = i; }
            }
            return best;
        }

        /// <summary>The 1000 cc superbike: about 190 hp, 275 kg with the rider, a long wheelbase and a low tuck; 300 km/h on a long straight and a wheelie in the first two gears.</summary>
        public static BikeSpec Superbike() { return new BikeSpec { id = "argo1000", name = "Argo 1000", klass = "Superbike" }; }

        /// <summary>The 600 cc supersport: light and quick to turn but with half the punch; it revs to 16000.</summary>
        public static BikeSpec Supersport()
        {
            return new BikeSpec
            {
                id = "argo600", name = "Argo 600", klass = "Supersport", meshStyle = 1, skin = 1,
                bikeMass = 187f, riderMass = 75f, wheelbase = 1.375f, cgFromRear = 0.68f, cgHeight = 0.61f, cgHeightTuck = 0.57f, wheelRadius = 0.305f,
                rpmCurve = new[] { 1400f, 3000f, 5000f, 7000f, 9000f, 11000f, 12500f, 13500f, 14500f, 15500f, 16000f },
                torqueCurve = new[] { 38f, 50f, 58f, 62f, 66f, 69f, 70f, 68f, 62f, 54f, 40f },
                idleRpm = 1400f, launchRpm = 9000f, redlineRpm = 15500f, limiterRpm = 16000f, engineInertia = 0.085f, engineBrake = 17f, slipperTorque = 40f,
                gears = new[] { 2.58f, 1.95f, 1.60f, 1.36f, 1.20f, 1.08f }, primaryRatio = 1.70f, finalRatio = 2.93f, upshiftRpm = 14400f, downshiftRpm = 7400f,
                brakeFront = 1350f, brakeRear = 430f, cdA = 0.47f, cdATuck = 0.34f,
                muFront = 1.29f, muRear = 1.33f, frameLeanMax = 54f * RacingMath.Deg2Rad, rollOmegaLow = 14f, rollOmegaHigh = 9.3f, rollRateLow = 2.1f, rollRateHigh = 1.45f,
                forkRate = 10500f, shockRate = 19000f,
            };
        }

        /// <summary>The 750 cc roadster: no fairing (more drag, no tuck to speak of), a torquey engine and a taller seat; easy to ride, slower at the end of the straight.</summary>
        public static BikeSpec Roadster()
        {
            return new BikeSpec
            {
                id = "vigile750", name = "Vigile 750", klass = "Roadster", meshStyle = 2, skin = 3,
                bikeMass = 205f, riderMass = 75f, wheelbase = 1.43f, cgFromRear = 0.72f, cgHeight = 0.66f, cgHeightTuck = 0.64f, wheelRadius = 0.31f,
                rpmCurve = new[] { 1200f, 2500f, 4000f, 5500f, 7000f, 8500f, 9500f, 10500f, 11500f, 12000f },
                torqueCurve = new[] { 60f, 72f, 80f, 84f, 86f, 86f, 83f, 76f, 64f, 52f },
                idleRpm = 1200f, launchRpm = 6000f, redlineRpm = 11500f, limiterRpm = 12000f, engineInertia = 0.13f, engineBrake = 26f, slipperTorque = 50f,
                gears = new[] { 2.50f, 1.82f, 1.46f, 1.24f, 1.10f, 0.98f }, primaryRatio = 1.62f, finalRatio = 2.75f, upshiftRpm = 10800f, downshiftRpm = 5200f,
                brakeFront = 1300f, brakeRear = 460f, cdA = 0.62f, cdATuck = 0.55f,
                muFront = 1.25f, muRear = 1.30f, frameLeanMax = 50f * RacingMath.Deg2Rad, hangMax = 6f * RacingMath.Deg2Rad, rollOmegaLow = 12.5f, rollOmegaHigh = 8.6f, rollRateLow = 1.8f, rollRateHigh = 1.2f,
                forkRate = 11500f, shockRate = 21000f,
            };
        }

        /// <summary>The 125 GP two-stroke: 71 kg of bike under a 62 kg rider, 48 hp that only wake up above 9000 rpm, a six-speed box with the gears close together, and tires that lean to 58 degrees.</summary>
        public static BikeSpec Gp125()
        {
            return new BikeSpec
            {
                id = "scintilla125", name = "Scintilla 125", klass = "125 GP", meshStyle = 1, meshScale = 0.90f, skin = 4, engine = EngineKind.TwoStroke125,
                bikeMass = 71f, riderMass = 62f, wheelbase = 1.27f, cgFromRear = 0.61f, cgHeight = 0.55f, cgHeightTuck = 0.51f, wheelRadius = 0.285f, wheelInertiaRear = 0.55f, wheelInertiaFront = 0.45f,
                rpmCurve = new[] { 3000f, 5000f, 7000f, 8500f, 9500f, 10500f, 11000f, 11500f, 12000f, 12500f, 13500f },
                torqueCurve = new[] { 5f, 7f, 10f, 16f, 24f, 31f, 32f, 31f, 26f, 14f, 5f },
                idleRpm = 3200f, launchRpm = 9500f, redlineRpm = 12500f, limiterRpm = 13000f, engineInertia = 0.025f, engineBrake = 6f, slipperTorque = 12f,
                gears = new[] { 3.07f, 2.23f, 1.77f, 1.46f, 1.26f, 1.11f }, primaryRatio = 2.10f, finalRatio = 2.60f, driveEfficiency = 0.93f, shiftTime = 0.10f, upshiftRpm = 12200f, downshiftRpm = 8200f,
                brakeFront = 800f, brakeRear = 250f, cdA = 0.34f, cdATuck = 0.24f,
                muFront = 1.28f, muRear = 1.30f, frameLeanMax = 58f * RacingMath.Deg2Rad, hangMax = 7f * RacingMath.Deg2Rad, minTurnRadius = 4.0f,
                rollOmegaLow = 16f, rollOmegaHigh = 10.5f, rollRateLow = 2.5f, rollRateHigh = 1.75f, forkRate = 7000f, shockRate = 12000f,
            };
        }

        /// <summary>The 675 triple: a middleweight that is light and nimble, 126 hp at 11500 rpm and a flat, forgiving torque curve (the howl of three cylinders).</summary>
        public static BikeSpec Triple675()
        {
            return new BikeSpec
            {
                id = "tigre675", name = "Tigre 675", klass = "Supersport", meshStyle = 1, meshScale = 0.98f, skin = 5, engine = EngineKind.Triple,
                bikeMass = 181f, riderMass = 75f, wheelbase = 1.375f, cgFromRear = 0.68f, cgHeight = 0.61f, cgHeightTuck = 0.57f, wheelRadius = 0.305f,
                rpmCurve = new[] { 1400f, 3000f, 5000f, 7000f, 9000f, 10500f, 11500f, 12500f, 13500f, 14500f, 15000f },
                torqueCurve = new[] { 48f, 64f, 72f, 76f, 80f, 80f, 78f, 70f, 58f, 42f, 30f },
                idleRpm = 1400f, launchRpm = 8500f, redlineRpm = 14000f, limiterRpm = 14500f, engineInertia = 0.08f, engineBrake = 16f, slipperTorque = 38f,
                gears = new[] { 2.68f, 2.08f, 1.74f, 1.49f, 1.33f, 1.20f }, primaryRatio = 1.70f, finalRatio = 2.90f, upshiftRpm = 13500f, downshiftRpm = 7200f,
                brakeFront = 1300f, brakeRear = 430f, cdA = 0.47f, cdATuck = 0.34f,
                muFront = 1.30f, muRear = 1.34f, frameLeanMax = 54f * RacingMath.Deg2Rad, minTurnRadius = 4.6f,
                rollOmegaLow = 14.5f, rollOmegaHigh = 9.6f, rollRateLow = 2.2f, rollRateHigh = 1.5f, forkRate = 10500f, shockRate = 19000f,
            };
        }

        /// <summary>The 1200 V-twin: 184 hp but above all a wall of torque from 3000 rpm (148 Nm), a heavy flywheel, a throb instead of a scream; it wheelies in the first three gears and likes to be ridden smooth.</summary>
        public static BikeSpec Twin1200()
        {
            return new BikeSpec
            {
                id = "duca1200", name = "Duca 1200", klass = "Superbike", meshStyle = 0, meshScale = 1.02f, skin = 10, engine = EngineKind.V2,
                bikeMass = 192f, riderMass = 75f, wheelbase = 1.43f, cgFromRear = 0.71f, cgHeight = 0.63f, cgHeightTuck = 0.59f, wheelRadius = 0.312f,
                rpmCurve = new[] { 1300f, 2500f, 4000f, 5500f, 7000f, 8000f, 9000f, 9500f, 10000f, 10500f, 11000f },
                torqueCurve = new[] { 75f, 105f, 125f, 138f, 145f, 148f, 146f, 140f, 126f, 100f, 70f },
                idleRpm = 1200f, launchRpm = 6000f, redlineRpm = 10300f, limiterRpm = 10800f, engineInertia = 0.16f, engineBrake = 34f, slipperTorque = 70f,
                gears = new[] { 2.29f, 1.70f, 1.37f, 1.14f, 1.00f, 0.89f }, primaryRatio = 1.60f, finalRatio = 2.80f, upshiftRpm = 9800f, downshiftRpm = 5200f,
                brakeFront = 1500f, brakeRear = 480f, cdA = 0.52f, cdATuck = 0.38f,
                muFront = 1.30f, muRear = 1.36f, frameLeanMax = 52f * RacingMath.Deg2Rad, minTurnRadius = 4.9f,
                rollOmegaLow = 12.5f, rollOmegaHigh = 8.0f, rollRateLow = 1.85f, rollRateHigh = 1.2f, forkRate = 11500f, shockRate = 21000f,
            };
        }

        /// <summary>The 1300 hypersport: the heaviest and the fastest in a straight line (about 195 hp, 300 km/h), a long wheelbase and a lazy turn-in; it does not like tight corners.</summary>
        public static BikeSpec Hyper1300()
        {
            return new BikeSpec
            {
                id = "gigante1300", name = "Gigante 1300", klass = "Hypersport", meshStyle = 0, meshScale = 1.06f, skin = 9, engine = EngineKind.Inline4,
                bikeMass = 230f, riderMass = 75f, wheelbase = 1.485f, cgFromRear = 0.73f, cgHeight = 0.64f, cgHeightTuck = 0.60f, wheelRadius = 0.315f,
                rpmCurve = new[] { 1300f, 2500f, 4000f, 5500f, 7000f, 8000f, 9000f, 9800f, 10500f, 11000f, 11500f },
                torqueCurve = new[] { 95f, 120f, 140f, 153f, 160f, 158f, 150f, 142f, 125f, 95f, 60f },
                idleRpm = 1200f, launchRpm = 6500f, redlineRpm = 10800f, limiterRpm = 11300f, engineInertia = 0.15f, engineBrake = 30f, slipperTorque = 70f,
                gears = new[] { 2.35f, 1.71f, 1.36f, 1.12f, 0.97f, 0.85f }, primaryRatio = 1.60f, finalRatio = 2.80f, upshiftRpm = 10300f, downshiftRpm = 5500f,
                brakeFront = 1550f, brakeRear = 520f, cdA = 0.49f, cdATuck = 0.335f,
                muFront = 1.28f, muRear = 1.36f, frameLeanMax = 50f * RacingMath.Deg2Rad, hangMax = 6f * RacingMath.Deg2Rad, minTurnRadius = 5.4f,
                rollOmegaLow = 11f, rollOmegaHigh = 7.4f, rollRateLow = 1.55f, rollRateHigh = 1.05f, forkRate = 12500f, shockRate = 23000f,
            };
        }

        /// <summary>The 500 GP two-stroke V4: 128 kg of bike and 190 hp that arrive all at once above 9000 rpm, slick tires, carbon brakes; the fastest and the hardest to ride of them all.</summary>
        public static BikeSpec Gp500()
        {
            return new BikeSpec
            {
                id = "rombo500", name = "Rombo 500", klass = "GP 500", meshStyle = 1, meshScale = 0.97f, skin = 7, engine = EngineKind.TwoStroke500,
                bikeMass = 128f, riderMass = 70f, wheelbase = 1.38f, cgFromRear = 0.66f, cgHeight = 0.60f, cgHeightTuck = 0.55f, wheelRadius = 0.305f, wheelInertiaRear = 0.85f, wheelInertiaFront = 0.65f,
                rpmCurve = new[] { 4000f, 6000f, 8000f, 9000f, 10000f, 11000f, 12000f, 12500f, 13000f, 14000f, 14500f },
                torqueCurve = new[] { 18f, 25f, 40f, 70f, 100f, 112f, 113f, 100f, 70f, 20f, 8f },
                idleRpm = 3500f, launchRpm = 10000f, redlineRpm = 13500f, limiterRpm = 14000f, engineInertia = 0.05f, engineBrake = 8f, slipperTorque = 25f,
                gears = new[] { 2.29f, 1.70f, 1.37f, 1.14f, 1.00f, 0.89f }, primaryRatio = 1.80f, finalRatio = 2.90f, upshiftRpm = 13000f, downshiftRpm = 9000f,
                brakeFront = 1800f, brakeRear = 520f, cdA = 0.42f, cdATuck = 0.30f,
                muFront = 1.46f, muRear = 1.50f, slipPeakRatio = 0.13f, frameLeanMax = 56f * RacingMath.Deg2Rad, hangMax = 7.5f * RacingMath.Deg2Rad, minTurnRadius = 4.5f,
                rollOmegaLow = 15f, rollOmegaHigh = 10f, rollRateLow = 2.3f, rollRateHigh = 1.6f, forkRate = 9500f, shockRate = 17000f,
            };
        }

        /// <summary>The 1000 naked: a superbike engine (160 hp) with no fairing, so a lot of drag and no tuck to hide in; light, wide handlebars, a bike for corners more than for straights.</summary>
        public static BikeSpec Naked1000()
        {
            return new BikeSpec
            {
                id = "corsaro1000", name = "Corsaro 1000", klass = "Roadster", meshStyle = 2, skin = 6, engine = EngineKind.Inline4,
                bikeMass = 186f, riderMass = 75f, wheelbase = 1.42f, cgFromRear = 0.705f, cgHeight = 0.66f, cgHeightTuck = 0.64f, wheelRadius = 0.31f,
                rpmCurve = new[] { 1300f, 2500f, 4000f, 6000f, 8000f, 9000f, 10000f, 11000f, 12000f, 12500f, 13000f },
                torqueCurve = new[] { 58f, 82f, 96f, 106f, 112f, 112f, 108f, 104f, 90f, 66f, 42f },
                idleRpm = 1300f, launchRpm = 7000f, redlineRpm = 12500f, limiterRpm = 13000f, engineInertia = 0.12f, engineBrake = 24f, slipperTorque = 52f,
                gears = new[] { 2.49f, 1.96f, 1.64f, 1.42f, 1.27f, 1.16f }, primaryRatio = 1.60f, finalRatio = 2.87f, upshiftRpm = 12000f, downshiftRpm = 6200f,
                brakeFront = 1400f, brakeRear = 470f, cdA = 0.62f, cdATuck = 0.55f,
                muFront = 1.28f, muRear = 1.33f, frameLeanMax = 52f * RacingMath.Deg2Rad, minTurnRadius = 4.8f,
                rollOmegaLow = 13f, rollOmegaHigh = 8.4f, rollRateLow = 1.95f, rollRateHigh = 1.28f, forkRate = 11200f, shockRate = 20500f,
            };
        }
    }
}
