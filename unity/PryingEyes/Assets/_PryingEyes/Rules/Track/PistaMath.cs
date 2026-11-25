using System;

namespace PryingEyes.Pista
{
    /// <summary>Small math helpers for the pure track code (bike physics, circuit, rider AI, race rules). Nothing here may use UnityEngine: these files are compiled by
    /// tools/tracklab from the same paths, so the rules are checked without Unity. Angles are radians; heading 0 looks along +z and grows toward +x, like the player's yaw.</summary>
    public static class PM
    {
        public const float Pi = 3.14159265f, TwoPi = 6.28318531f, HalfPi = 1.57079633f, Deg2Rad = 0.01745329f, Rad2Deg = 57.2957795f, G = 9.81f;

        public static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
        public static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static float InvLerp(float a, float b, float v) { return Math.Abs(b - a) < 1e-9f ? 0f : (v - a) / (b - a); }
        public static float Sqr(float v) { return v * v; }
        public static float Abs(float v) { return v < 0f ? -v : v; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Sign(float v) { return v < 0f ? -1f : 1f; }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static float Sin(float v) { return (float)Math.Sin(v); }
        public static float Cos(float v) { return (float)Math.Cos(v); }
        public static float Tan(float v) { return (float)Math.Tan(v); }
        public static float Atan(float v) { return (float)Math.Atan(v); }
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Asin(float v) { return (float)Math.Asin(Clamp(v, -1f, 1f)); }
        public static float Pow(float a, float b) { return (float)Math.Pow(a, b); }
        public static float Exp(float v) { return (float)Math.Exp(v); }

        /// <summary>The fraction of the way to a target covered in 'dt' by an exponential approach with rate 'k' per second (frame-rate independent smoothing).</summary>
        public static float Ease(float k, float dt) { return 1f - (float)Math.Exp(-k * dt); }

        /// <summary>An angle wrapped into [-pi, pi].</summary>
        public static float WrapPi(float a)
        {
            a %= TwoPi;
            if (a > Pi) a -= TwoPi; else if (a < -Pi) a += TwoPi;
            return a;
        }

        public static float MoveToward(float cur, float target, float maxDelta)
        {
            float d = target - cur;
            if (d > maxDelta) return cur + maxDelta;
            if (d < -maxDelta) return cur - maxDelta;
            return target;
        }

        public static float SmoothStep(float a, float b, float x)
        {
            float t = Clamp01(InvLerp(a, b, x));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Linear interpolation in a table of (x, y) pairs given as two arrays (x ascending); clamped at both ends.</summary>
        public static float Table(float[] xs, float[] ys, float x)
        {
            if (x <= xs[0]) return ys[0];
            int n = xs.Length;
            if (x >= xs[n - 1]) return ys[n - 1];
            int i = 1;
            while (i < n - 1 && xs[i] < x) i++;
            float t = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
            return ys[i - 1] + (ys[i] - ys[i - 1]) * t;
        }

        /// <summary>The tire friction curve, normalised: 0 at no slip, 1 at the peak (normalised slip 1), settling at 0.707 when the tire slides. A Pacejka-like sin(C atan(B s)) with C = 1.5.</summary>
        public static float TireCurve(float s)
        {
            s = s < 0f ? -s : s;
            return (float)Math.Sin(1.5 * Math.Atan(1.7320508 * s));
        }
    }

    /// <summary>A small deterministic random generator (xorshift32): a circuit, its riders and every race event are the same on every machine for a given seed.</summary>
    public struct Rng
    {
        uint s;

        public Rng(int seed)
        {
            s = (uint)seed * 2654435761u + 0x9E3779B9u;
            if (s == 0) s = 0x1234567u;
            for (int i = 0; i < 4; i++) Next();
        }

        public uint Next()
        {
            uint x = s;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            s = x;
            return x;
        }

        /// <summary>A float in [0, 1).</summary>
        public float Value() { return (Next() >> 8) / 16777216f; }
        public float Range(float a, float b) { return a + (b - a) * Value(); }
        /// <summary>An int in [0, n).</summary>
        public int Int(int n) { return n <= 1 ? 0 : (int)(Next() % (uint)n); }
        public bool Chance(float p) { return Value() < p; }
    }
}
