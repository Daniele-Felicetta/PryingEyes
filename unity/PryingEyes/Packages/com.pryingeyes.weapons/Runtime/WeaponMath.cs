namespace Production.Weapons
{
    /// <summary>The few numeric helpers the weapon rules need, with the meaning of the engine's own (so that the same expression gives the same number whichever side runs it).
    /// Internal: the package's public surface is the rules, not a maths library.</summary>
    internal static class WeaponMath
    {
        /// <summary>An integer held between <paramref name="min"/> and <paramref name="max"/> (the same as the engine's integer clamp).</summary>
        internal static int Clamp(int value, int min, int max)
        {
            if (value < min) value = min;
            else if (value > max) value = max;
            return value;
        }
    }
}
