namespace Production.Weapons
{
    /// <summary>How the holder stands, in the three numbers the spread of a weapon depends on: how fast it moves (0 still, 1 at a run), whether it is off the ground (0 or 1)
    /// and whether it crouches (0 or 1).</summary>
    public readonly struct AimStance
    {
        /// <summary>Speed in metres per second at which the movement counts in full.</summary>
        public const float RunSpeed = 5.6f;

        public readonly float Move, Air, Crouch;
        public readonly bool Crouched;

        public AimStance(float speed, bool grounded, bool crouched)
        {
            Move = WeaponMath.Clamp01(speed / RunSpeed);
            Air = grounded ? 0f : 1f;
            Crouch = crouched ? 1f : 0f;
            Crouched = crouched;
        }
    }

    /// <summary>The cone a shot can fall in. The angles are half-angles in degrees.</summary>
    public static class WeaponSpread
    {
        /// <summary>The half-opening of the cone: the weapon's own, between held at the hip and aiming, wider when moving or in the air, narrower when crouching, plus the bloom the last
        /// shots left (less of it when aiming), and then the multipliers of the holder's powers and of the vehicle the holder is riding.</summary>
        public static float Degrees(WeaponDef d, AimStance stance, float ads, float bloom, float magicMultiplier, float rideMultiplier)
        {
            float baseDeg = WeaponMath.Lerp(d.hip, d.ads, ads);
            float mv = 1f + d.moveSpread * stance.Move * (1f - ads * 0.6f);
            float c = stance.Crouched ? 0.72f : 1f;
            float air = 1f + stance.Air * 1.6f;
            return (baseDeg * mv * c * air + bloom * WeaponMath.Lerp(1f, 0.5f, ads)) * magicMultiplier * rideMultiplier;
        }

        /// <summary>The spread of the first shot after a pause, aiming and standing still: kept tight (a precision weapon is precise). Any other shot keeps <paramref name="spread"/>.</summary>
        public static float FirstShot(float spread, float secondsSinceLastShot, float ads, float move)
        {
            if (secondsSinceLastShot > 0.4f && ads > 0.8f && move < 0.1f) spread *= 0.35f;
            return spread;
        }

        /// <summary>The radius of the circle, one unit in front of the muzzle, that a cone of <paramref name="degrees"/> cuts: the caller picks a point in it and turns it into a direction.</summary>
        public static float ConeRadius(float degrees)
        {
            return WeaponMath.Tan(degrees * WeaponMath.Deg2Rad);
        }
    }

    /// <summary>The dispersion that every shot leaves and that goes away by itself.</summary>
    public static class WeaponBloom
    {
        /// <summary>The bloom after a shot: what each shot adds, up to the weapon's largest.</summary>
        public static float Grown(float bloom, WeaponDef d)
        {
            return WeaponMath.Min(d.bloomMax, bloom + d.bloomShot);
        }

        /// <summary>The bloom after <paramref name="dt"/> seconds: it falls by the weapon's rate, twice as fast when fully aiming, and never below zero.</summary>
        public static float Recovered(float bloom, WeaponDef d, float dt, float ads)
        {
            return WeaponMath.Max(0f, bloom - d.bloomRecover * dt * (1f + ads));
        }
    }
}
