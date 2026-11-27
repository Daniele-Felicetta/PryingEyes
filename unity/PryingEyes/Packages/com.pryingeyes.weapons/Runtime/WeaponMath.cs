using System;

namespace Production.Weapons
{
    /// <summary>The few numeric helpers the weapon rules need, each with the meaning of the engine's own (so that the same expression gives the same number whichever side runs it):
    /// the same tests in the same order, the same single-precision conversions. Internal: the package's public surface is the rules, not a maths library.</summary>
    internal static class WeaponMath
    {
        /// <summary>Degrees to radians, made the way the engine makes its constant: (pi * 2) / 360 in single precision.</summary>
        internal const float Deg2Rad = ((float)Math.PI * 2F) / 360F;

        /// <summary>An integer held between <paramref name="min"/> and <paramref name="max"/> (the same as the engine's integer clamp).</summary>
        internal static int Clamp(int value, int min, int max)
        {
            if (value < min) value = min;
            else if (value > max) value = max;
            return value;
        }

        internal static float Clamp(float value, float min, float max)
        {
            if (value < min) value = min;
            else if (value > max) value = max;
            return value;
        }

        internal static float Clamp01(float value)
        {
            if (value < 0F) return 0F;
            else if (value > 1F) return 1F;
            else return value;
        }

        /// <summary>The engine's linear interpolation: the fraction is held between 0 and 1.</summary>
        internal static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        internal static float Min(float a, float b) => a < b ? a : b;
        internal static float Max(float a, float b) => a > b ? a : b;
        internal static int Min(int a, int b) => a < b ? a : b;

        internal static float Abs(float f) => Math.Abs(f);

        /// <summary>Plus one for zero and above, minus one below (the engine's, not <see cref="Math.Sign(float)"/>, which gives 0 for 0).</summary>
        internal static float Sign(float f) => f >= 0F ? 1F : -1F;

        internal static float Sin(float f) => (float)Math.Sin(f);
        internal static float Tan(float f) => (float)Math.Tan(f);
        internal static float Exp(float power) => (float)Math.Exp(power);

        internal static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Abs(target - current) <= maxDelta) return target;
            return current + Sign(target - current) * maxDelta;
        }
    }
}
