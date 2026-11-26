namespace Production.Racing
{
    /// <summary>What a hand does to the keys of the bike, in one place that has no engine so a test rides with the very same code as the game: the gas opens in a third of a second, the front and the rear brake
    /// build up, and the steering grows the longer the key is held, slower the faster the bike goes (a tap is a small correction, not a fall). A key is all or nothing and a full lean at 200 km/h is a fall, so
    /// above 36 km/h the steering keys ask for less than a full lean: the share they can reach goes from 1 at 10 m/s down to <see cref="CapFloor"/> at 25 m/s (the controller's stick is analog and is not capped).</summary>
    public sealed class KeyShaper
    {
        /// <summary>The speeds (m/s) between which the share of the lean that the steering keys can ask for falls from 1 to <see cref="CapFloor"/>.</summary>
        public const float CapSlow = 10f, CapFast = 25f;
        /// <summary>The share of the tightest turn a steering key reaches at <see cref="CapFast"/> and above.</summary>
        public const float CapFloor = 0.7f;

        /// <summary>What the keys are doing now: gas, front brake, rear brake (0 to 1) and steering (-1 left, 1 right: the fraction of the tightest turn the bike can make at this speed).</summary>
        public float thr, bf, br, steer;

        /// <summary>The share a steering key reaches at <see cref="CapFast"/> and above. It is <see cref="CapFloor"/>; a test sets 1 on a shaper to see what the cap does.</summary>
        public float capFloor = CapFloor;

        /// <summary>The share of the lean a steering key can reach at this speed (m/s), when the cap ends at 'floor'.</summary>
        public static float SteerCap(float speed, float floor) { return RacingMath.Lerp(1f, floor, RacingMath.Clamp01((speed - CapSlow) / (CapFast - CapSlow))); }

        /// <summary>The share of the lean a steering key can reach at this speed (m/s).</summary>
        public static float SteerCap(float speed) { return SteerCap(speed, CapFloor); }

        public void Reset() { thr = bf = br = steer = 0f; }

        /// <summary>One step of 'dt' seconds with the keys as they are held now, at 'speed' (m/s).</summary>
        public void Step(bool up, bool down, bool rear, bool left, bool right, float speed, float dt)
        {
            thr = RacingMath.MoveToward(thr, up ? 1f : 0f, (up ? 3.2f : 7f) * dt);
            bf = RacingMath.MoveToward(bf, down ? 1f : 0f, (down ? 2.4f : 9f) * dt);
            br = RacingMath.MoveToward(br, rear ? 1f : 0f, (rear ? 4f : 9f) * dt);
            float target = ((right ? 1f : 0f) - (left ? 1f : 0f)) * SteerCap(speed, capFloor);
            float k = RacingMath.Clamp01(speed / 70f);
            float rate = RacingMath.Lerp(4.6f, 1.7f, k);
            if (target != 0f && RacingMath.Sign(target) != RacingMath.Sign(steer) && steer != 0f) rate *= 2.2f;       // reversing is quicker than building up
            if (target == 0f) rate = RacingMath.Lerp(7.5f, 4.5f, k);
            steer = RacingMath.MoveToward(steer, target, rate * dt);
        }
    }
}
