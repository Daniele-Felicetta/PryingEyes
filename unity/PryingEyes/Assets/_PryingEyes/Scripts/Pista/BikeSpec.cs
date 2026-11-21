using System;

namespace PryingEyes.Pista
{
    /// <summary>Everything that makes one motorcycle different from another, in SI units: mass and geometry, engine torque curve and gearbox, brakes, aerodynamics, tires and how far it can lean.
    /// BikeModel reads it; the three presets (Bikes) are real-feeling machines of three classes: a 1000 cc superbike, a 600 cc supersport and a 750 cc roadster. Pure code, no Unity.</summary>
    public sealed class BikeSpec
    {
        public string id = "argo1000", name = "Argo 1000", klass = "Superbike";

        // ---- mass and geometry (the rider is part of the total, seated and leaning with the bike)
        public float bikeMass = 200f, riderMass = 75f;
        public float wheelbase = 1.40f;
        /// <summary>Distance from the centre of gravity to the rear axle (m): the front carries cgFromRear / wheelbase of the weight at rest.</summary>
        public float cgFromRear = 0.70f;
        /// <summary>Height of the centre of gravity with the rider upright, and tucked behind the screen (m).</summary>
        public float cgHeight = 0.62f, cgHeightTuck = 0.58f;
        public float wheelRadius = 0.31f;
        public float wheelInertiaRear = 0.95f, wheelInertiaFront = 0.75f;

        // ---- engine (crankshaft torque in Nm against rpm) and gearbox
        public float[] rpmCurve = { 1300f, 2500f, 4000f, 6000f, 8000f, 10000f, 11000f, 12000f, 13000f, 14000f, 14500f };
        public float[] torqueCurve = { 52f, 74f, 88f, 100f, 108f, 115f, 116f, 112f, 104f, 92f, 70f };
        public float idleRpm = 1300f, launchRpm = 7500f, redlineRpm = 14000f, limiterRpm = 14500f;
        /// <summary>Spinning inertia of the crank, clutch and gearbox input shaft (kg m2): it adds to the wheel's inertia through the gear ratio squared.</summary>
        public float engineInertia = 0.12f;
        /// <summary>Compression braking of the closed throttle (Nm at the crank at 10000 rpm); a slipper clutch caps what reaches the wheel.</summary>
        public float engineBrake = 24f, slipperTorque = 55f;
        /// <summary>Gearbox ratios, first to last, primary reduction and final (chain) reduction.</summary>
        public float[] gears = { 2.60f, 1.95f, 1.58f, 1.33f, 1.17f, 1.04f };
        public float primaryRatio = 1.60f, finalRatio = 2.87f, driveEfficiency = 0.92f;
        public float shiftTime = 0.09f;
        /// <summary>Automatic gearbox: shift up at this engine speed, shift down below it in the lower gear.</summary>
        public float upshiftRpm = 13100f, downshiftRpm = 6200f;

        // ---- brakes: peak torque on each wheel (Nm); the tires, not the discs, set the limit on a dry road
        public float brakeFront = 1500f, brakeRear = 480f;

        // ---- aerodynamics (CdA in m2: upright and tucked), air density 1.2
        public float cdA = 0.50f, cdATuck = 0.36f;

        // ---- tires: peak friction of each tire on clean asphalt, slip at which the peak happens
        public float muFront = 1.30f, muRear = 1.35f;
        public float slipPeakRatio = 0.12f;

        // ---- lean and steering
        /// <summary>The frame lean at which pegs and exhaust touch the road (rad), how much further the rider's body shift lets the centre of gravity go (rad), tightest turn at a crawl (m).</summary>
        public float frameLeanMax = 52f * PM.Deg2Rad, hangMax = 6.5f * PM.Deg2Rad, minTurnRadius = 4.8f;
        /// <summary>How fast the bike rolls into a turn: natural frequency (rad/s) at a crawl and at 80 m/s, and the top roll rate (rad/s) at a crawl and at 80 m/s.</summary>
        public float rollOmegaLow = 13f, rollOmegaHigh = 8.2f, rollRateLow = 1.9f, rollRateHigh = 1.25f;

        // ---- suspension (N/m): the dive and squat that load transfer causes
        public float forkRate = 11000f, shockRate = 20000f;

        // ---- derived
        public float Mass { get { return bikeMass + riderMass; } }
        public float CgFromFront { get { return wheelbase - cgFromRear; } }
        public float TotalRatio(int gear) { return gear < 1 || gear > gears.Length ? 0f : primaryRatio * gears[gear - 1] * finalRatio; }
        public int GearCount { get { return gears.Length; } }

        /// <summary>The crankshaft torque at full throttle (Nm).</summary>
        public float TorqueAt(float rpm) { return PM.Table(rpmCurve, torqueCurve, rpm); }

        /// <summary>The crankshaft power at full throttle (kW).</summary>
        public float PowerKw(float rpm) { return TorqueAt(rpm) * rpm * PM.TwoPi / 60f / 1000f; }

        /// <summary>The highest crankshaft power over the rev range (kW) and the engine speed where it happens.</summary>
        public float PeakPowerKw(out float atRpm)
        {
            float best = 0f; atRpm = idleRpm;
            for (float r = idleRpm; r <= redlineRpm; r += 50f)
            {
                float p = PowerKw(r);
                if (p > best) { best = p; atRpm = r; }
            }
            return best;
        }

        public BikeSpec Clone() { return (BikeSpec)MemberwiseClone(); }
    }

    /// <summary>The bikes of the track mode.</summary>
    public static class Bikes
    {
        public const int Count = 3;

        public static BikeSpec Get(int index)
        {
            switch (PM.Clamp(index, 0, Count - 1))
            {
                case 1: return Supersport();
                case 2: return Roadster();
                default: return Superbike();
            }
        }

        /// <summary>The 1000 cc superbike: about 190 hp, 275 kg with the rider, a long wheelbase and a low tuck; 300 km/h on a long straight and a wheelie in the first two gears.</summary>
        public static BikeSpec Superbike() { return new BikeSpec(); }

        /// <summary>The 600 cc supersport: light and quick to turn but with half the punch; it revs to 16000.</summary>
        public static BikeSpec Supersport()
        {
            return new BikeSpec
            {
                id = "argo600", name = "Argo 600", klass = "Supersport",
                bikeMass = 187f, riderMass = 75f, wheelbase = 1.375f, cgFromRear = 0.68f, cgHeight = 0.61f, cgHeightTuck = 0.57f, wheelRadius = 0.305f,
                rpmCurve = new[] { 1400f, 3000f, 5000f, 7000f, 9000f, 11000f, 12500f, 13500f, 14500f, 15500f, 16000f },
                torqueCurve = new[] { 38f, 50f, 58f, 62f, 66f, 69f, 70f, 68f, 62f, 54f, 40f },
                idleRpm = 1400f, launchRpm = 9000f, redlineRpm = 15500f, limiterRpm = 16000f, engineInertia = 0.085f, engineBrake = 17f, slipperTorque = 40f,
                gears = new[] { 2.58f, 1.95f, 1.60f, 1.36f, 1.20f, 1.08f }, primaryRatio = 1.70f, finalRatio = 2.93f, upshiftRpm = 14400f, downshiftRpm = 7400f,
                brakeFront = 1350f, brakeRear = 430f, cdA = 0.47f, cdATuck = 0.34f,
                muFront = 1.29f, muRear = 1.33f, frameLeanMax = 54f * PM.Deg2Rad, rollOmegaLow = 14f, rollOmegaHigh = 9.3f, rollRateLow = 2.1f, rollRateHigh = 1.45f,
                forkRate = 10500f, shockRate = 19000f,
            };
        }

        /// <summary>The 750 cc roadster: no fairing (more drag, no tuck to speak of), a torquey engine and a taller seat; easy to ride, slower at the end of the straight.</summary>
        public static BikeSpec Roadster()
        {
            return new BikeSpec
            {
                id = "vigile750", name = "Vigile 750", klass = "Roadster",
                bikeMass = 205f, riderMass = 75f, wheelbase = 1.43f, cgFromRear = 0.72f, cgHeight = 0.66f, cgHeightTuck = 0.64f, wheelRadius = 0.31f,
                rpmCurve = new[] { 1200f, 2500f, 4000f, 5500f, 7000f, 8500f, 9500f, 10500f, 11500f, 12000f },
                torqueCurve = new[] { 60f, 72f, 80f, 84f, 86f, 86f, 83f, 76f, 64f, 52f },
                idleRpm = 1200f, launchRpm = 6000f, redlineRpm = 11500f, limiterRpm = 12000f, engineInertia = 0.13f, engineBrake = 26f, slipperTorque = 50f,
                gears = new[] { 2.50f, 1.82f, 1.46f, 1.24f, 1.10f, 0.98f }, primaryRatio = 1.62f, finalRatio = 2.75f, upshiftRpm = 10800f, downshiftRpm = 5200f,
                brakeFront = 1300f, brakeRear = 460f, cdA = 0.62f, cdATuck = 0.55f,
                muFront = 1.25f, muRear = 1.30f, frameLeanMax = 50f * PM.Deg2Rad, hangMax = 6f * PM.Deg2Rad, rollOmegaLow = 12.5f, rollOmegaHigh = 8.6f, rollRateLow = 1.8f, rollRateHigh = 1.2f,
                forkRate = 11500f, shockRate = 21000f,
            };
        }
    }
}
