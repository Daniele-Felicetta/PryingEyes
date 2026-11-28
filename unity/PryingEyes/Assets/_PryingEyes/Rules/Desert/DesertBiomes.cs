using System;

namespace PryingEyes.Deserto
{
    public enum Biome { Desert, Forest, Ice, Meadow }

    /// <summary>Continuous surface weights shared by terrain, vegetation, map and movement.</summary>
    public readonly struct BiomeMix
    {
        public readonly float Forest, Ice, Meadow;
        public float Desert => Math.Max(0f, 1f - Forest - Ice - Meadow);

        public BiomeMix(float forest, float ice, float meadow)
        {
            Forest = forest;
            Ice = ice;
            Meadow = meadow;
        }

        public Biome Dominant
        {
            get
            {
                float natural = Math.Max(Forest, Math.Max(Ice, Meadow));
                if (Desert >= natural) return Biome.Desert;
                if (natural == Ice) return Biome.Ice;
                return natural == Forest ? Biome.Forest : Biome.Meadow;
            }
        }
    }

    public static class DesertBiomes
    {
        public static (float x, float z) Landmark(Biome biome)
        {
            switch (biome)
            {
                case Biome.Forest: return (740f, 145f);
                case Biome.Ice: return (90f, 960f);
                case Biome.Meadow: return (-700f, 65f);
                default: DesertWorld.CathedralCentre(out float x, out float z); return (x, z);
            }
        }

        public static BiomeMix At(float x, float z, int seed)
        {
            DesertWorld.CathedralCentre(out float cx, out float cz);
            float dx = x - cx, dz = z - cz;
            float distance2 = dx * dx + dz * dz;
            if (distance2 <= 420f * 420f) return default;

            // Shift the large climate boundaries with two independent, continuous fields.
            float wx = dx + (DesertNoise.Value(x / 410f + 1.6f, z / 355f, seed + 9201) - 0.5f) * 236f;
            float wz = dz + (DesertNoise.Value(x / 345f, z / 460f - 2.3f, seed + 9202) - 0.5f) * 200f;
            float border = DesertNoise.Smooth(420f, 565f, (float)Math.Sqrt(distance2));
            float ice = DesertNoise.Smooth(550f, 795f, wz - Math.Abs(wx) * 0.21f) * border;
            float temperate = DesertNoise.Smooth(370f, 610f,
                Math.Abs(wx) + Math.Max(0f, wz) * 0.32f - Math.Max(0f, -wz - 310f) * 0.71f);
            temperate *= border * (1f - ice);
            float woodland = DesertNoise.Smooth(-165f, 225f, wx + 82f * (DesertNoise.Value(x / 290f, z / 290f, seed + 9203) - 0.5f));
            return new BiomeMix(temperate * woodland, ice, temperate * (1f - woodland));
        }

        public static float Relief(float x, float z, int seed, BiomeMix mix, float desertHeight)
        {
            if (mix.Desert >= 1f) return desertHeight;
            float hills = DesertNoise.Fbm(x / 245f + 6.8f, z / 214f - 4f, seed + 9211, 3);
            float bumps = DesertNoise.Value(x / 29f, z / 33f, seed + 9212);
            float forest = 2.6f + hills * 12f + bumps * 2.1f;
            float meadow = 2f + hills * hills * 34f + bumps * 1.5f;
            if (mix.Meadow > 0f)
            {
                float shoulders = DesertNoise.Fbm((x + z * 0.48f) / 310f, (z - x * 0.22f) / 490f, seed + 9221, 3);
                meadow += DesertNoise.Smooth(0.31f, 0.78f, shoulders) * 26f;
            }
            float ice = 0f;
            if (mix.Ice > 0f)
            {
                float ridges = DesertNoise.Ridged(x / 160f + z / 315f, z / 420f, seed + 9213, 2);
                float low = DesertNoise.Fbm(x / 280f, z / 212f, seed + 9214, 3);
                ice = 2.2f + 16f * low + 33f * ridges * ridges * DesertNoise.Smooth(0.27f, 0.68f, low);
                float crags = DesertNoise.Ridged(x / 105f, z / 146f, seed + 9224, 3);
                ice += IceSpur(x, z, seed) * DesertNoise.Smooth(0.65f, 0.97f, mix.Ice) * (20f + 62f * crags * crags);
            }
            return desertHeight * mix.Desert + (DesertWorld.GroundY + forest) * mix.Forest +
                (DesertWorld.GroundY + meadow) * mix.Meadow + (DesertWorld.GroundY + ice) * mix.Ice;
        }

        internal static float IceSpur(float x, float z, int seed)
        {
            float spine = 1190f + x * 0.19f + (DesertNoise.Value(x / 180f, 3.7f, seed + 9223) - 0.5f) * 122f;
            return 1f - DesertNoise.Smooth(20f, 168f, Math.Abs(z - spine));
        }

        public static float IceSheet(float x, float z, int seed, BiomeMix mix) =>
            mix.Ice * DesertNoise.Smooth(0.46f, 0.63f, DesertNoise.Value(x / 28f + 3f, z / 37f - 7f, seed + 9215));
    }
}
