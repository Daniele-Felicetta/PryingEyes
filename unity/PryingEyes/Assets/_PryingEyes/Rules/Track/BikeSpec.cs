using System;

namespace PryingEyes.Pista
{
    /// <summary>The kind of engine, which is what a bike sounds like (the loop the game plays at the engine speed): the four-cylinder scream, the throb of a V-twin, the howl of a triple, the ring of a
    /// two-stroke single and the buzz of a two-stroke V4.</summary>
    public enum EngineKind : byte { Inline4 = 0, V2 = 1, Triple = 2, TwoStroke125 = 3, TwoStroke500 = 4 }

    /// <summary>Everything that makes one motorcycle different from another, in SI units: mass and geometry, engine torque curve and gearbox, brakes, aerodynamics, tires and how far it can lean.
    /// BikeModel reads it; the three presets (Bikes) are real-feeling machines of three classes: a 1000 cc superbike, a 600 cc supersport and a 750 cc roadster. Pure code, no Unity.</summary>
    public sealed class BikeSpec
    {
        public string id = "bike", name = "Bike", klass = "";
        /// <summary>The level of tuning (Tuning.Apply sets it, and adds it to the key): 0 for the bike as it comes.</summary>
        public int tune;
        /// <summary>Which of the bikes drawn by code stands for it in the scene: 0 superbike, 1 supersport, 2 naked roadster (PistaBikeMesh).</summary>
        public int meshStyle;
        /// <summary>How big its picture is against the shared bikes (a 125 is a small bike, a hypersport a big one) and what its engine sounds like.</summary>
        public float meshScale = 1f;
        public EngineKind engine = EngineKind.Inline4;
        /// <summary>The colours it wears when it is the player's: an index into the liveries of the game (twelve of them), a different one for every bike, so that the roster is told apart at a glance.</summary>
        public int skin;

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
}
