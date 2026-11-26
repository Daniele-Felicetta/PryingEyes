namespace Production.Weapons
{
    /// <summary>The recoil of the view at a shot, in degrees. It grows a little with a burst, and aiming and crouching soften it. The caller supplies the random part (a number between
    /// -1 and 1), so that the rules draw nothing and the same inputs give the same kick; the way the view comes back is <see cref="ViewRecoil"/>.</summary>
    public static class WeaponRecoil
    {
        /// <summary>What softens the kick: aiming (down to 72 %), crouching (85 %) and the holder's own multiplier. <paramref name="crouch"/> is 1 when crouching, 0 when not.</summary>
        public static float Damping(float ads, float crouch, float recoilMultiplier)
        {
            return WeaponMath.Lerp(1f, 0.72f, ads) * (crouch > 0.5f ? 0.85f : 1f) * recoilMultiplier;
        }

        /// <summary>The vertical kick of a shot, up; <paramref name="streak"/> is the number of the shot in the burst (3.5 % more for each, up to the tenth).</summary>
        public static float Vertical(WeaponDef d, int streak, float damping)
        {
            return d.recV * (1f + 0.035f * WeaponMath.Min(streak, 10)) * damping;
        }

        /// <summary>The horizontal kick of a shot: a random part of <paramref name="noise"/> (-1 to 1) and a swing that depends on the number of the shot and on the weapon.</summary>
        public static float Horizontal(WeaponDef d, int streak, float noise, float damping)
        {
            return (noise * d.recH + WeaponMath.Sin(streak * 0.9f + d.id.Length) * d.recH * 0.45f) * damping;
        }
    }
}
