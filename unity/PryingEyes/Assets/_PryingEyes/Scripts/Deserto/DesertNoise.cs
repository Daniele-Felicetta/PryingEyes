using System;

namespace PryingEyes.Deserto
{
    /// <summary>Deterministic integer hash and smooth value noise. No UnityEngine: the desert's ground, sites and clock are pure functions of the
    /// world seed, so tools/desertlab tests them outside the game and every machine of a co-op session would build the same world.</summary>
    public static class DesertNoise
    {
        public static uint Hash(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)x * 0x85EBCA6Bu;
                h = (h << 13) | (h >> 19);
                h *= 0xC2B2AE35u;
                h ^= (uint)z * 0x27D4EB2Fu;
                h ^= h >> 15; h *= 0x85EBCA77u; h ^= h >> 13; h *= 0xC2B2AE3Du; h ^= h >> 16;
                return h;
            }
        }

        /// <summary>A hash in [0, 1).</summary>
        public static float Hash01(int x, int z, int seed) => (Hash(x, z, seed) >> 8) * (1f / 16777216f);

        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Smoothstep: 0 below a, 1 above b.</summary>
        public static float Smooth(float a, float b, float x)
        {
            float t = Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>Smooth value noise in [0, 1).</summary>
        public static float Value(float x, float z, int seed)
        {
            int xi = (int)Math.Floor(x), zi = (int)Math.Floor(z);
            float fx = x - xi, fz = z - zi;
            float a = Hash01(xi, zi, seed), b = Hash01(xi + 1, zi, seed), c = Hash01(xi, zi + 1, seed), d = Hash01(xi + 1, zi + 1, seed);
            float u = Fade(fx), v = Fade(fz);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        /// <summary>Fractal sum of value noise in [0, 1): each octave twice as fine and half as strong.</summary>
        public static float Fbm(float x, float z, int seed, int octaves = 4)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(x, z, seed + i * 131) * amp;
                norm += amp;
                x = x * 2.03f + 17.1f; z = z * 2.03f - 9.7f;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        /// <summary>Ridged noise in [0, 1]: sharp crests where the value noise crosses one half (the dune crests).</summary>
        public static float Ridged(float x, float z, int seed, int octaves = 3)
        {
            float sum = 0f, amp = 0.55f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Math.Abs(2f * Value(x, z, seed + i * 197) - 1f);
                sum += n * n * amp;
                norm += amp;
                x = x * 2.1f + 5.3f; z = z * 2.1f + 11.9f;
                amp *= 0.5f;
            }
            return sum / norm;
        }
    }
}
