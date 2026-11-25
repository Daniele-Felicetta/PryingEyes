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
    }
}
