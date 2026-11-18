using System;
using System.Collections.Generic;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    /// <summary>The things that lie on the sand between the places: tufts of dry grass, pebbles, rocks and boulders (tan, dark, red, white), standing slabs, cacti, bushes, dead trees,
    /// bones, broken columns, steles and cairns. Which of them, and how many, is up to the region (DesertWorld.RegionBlend).</summary>
    public enum Prop
    {
        Tuft, Pebbles, RockTan, RockDark, RockRed, RockWhite, BoulderTan, BoulderDark, BoulderRed, Slab,
        Cactus, Barrel, Bush, DeadTree, Bones, Column, Stele, Cairn,
        Tent, FirePit, Crate, Well, Obelisk, Windmill, Wagon, Count
    }

    public struct PropItem
    {
        public byte kind;
        public float x, y, z, yaw, scale;
    }

    /// <summary>Where the props lie, as a pure function of the seed: every 64 m square of the desert (the terrain's own grid) holds its own set, computed from the square's coordinates and
    /// the region only, so it is the same every time and a worker thread can make it. Nothing is placed on a site's footprint (and a few metres around), on the cathedral's steps or
    /// where the sand is steep. tools/desertlab checks it.</summary>
    public static class DesertScatterGen
    {
        public const float Chunk = 64f;
        /// <summary>How far (m) a group of props can reach beyond its square (the longest colonnade: 4 columns, 3.4 m apart, a little scattered).</summary>
        public const float Leak = 18f;
        /// <summary>How far (m) each tier of props is drawn: the small things close, the big ones (they can be seen from afar) out to the middle of the fog.</summary>
        public static readonly float[] TierRadius = { 150f, 280f, 520f };

        public static int Tier(Prop p)
        {
            switch (p)
            {
                case Prop.Tuft: case Prop.Pebbles: case Prop.Bones: case Prop.Cairn: case Prop.Barrel: case Prop.Crate: return 0;
                case Prop.RockTan: case Prop.RockDark: case Prop.RockRed: case Prop.RockWhite: case Prop.Cactus: case Prop.Bush:
                case Prop.Tent: case Prop.FirePit: case Prop.Well: case Prop.Wagon: return 1;
                default: return 2;
            }
        }

        /// <summary>The props that stop a walker: a vertical capsule (radius, height at scale 1) from the ground up. Zero radius: nothing to bump into.</summary>
        public static void Solid(Prop p, out float radius, out float height)
        {
            switch (p)
            {
                case Prop.BoulderTan: case Prop.BoulderDark: case Prop.BoulderRed: radius = 1.05f; height = 1.7f; return;
                case Prop.Slab: radius = 0.9f; height = 2.2f; return;
                case Prop.Cactus: radius = 0.32f; height = 2.4f; return;
                case Prop.DeadTree: radius = 0.3f; height = 3.2f; return;
                case Prop.Column: radius = 0.5f; height = 2.0f; return;
                case Prop.Stele: radius = 0.36f; height = 2.4f; return;
                case Prop.Tent: radius = 1.25f; height = 1.5f; return;
                case Prop.FirePit: radius = 0.75f; height = 0.45f; return;
                case Prop.Crate: radius = 0.85f; height = 1.1f; return;
                case Prop.Well: radius = 1.0f; height = 1.2f; return;
                case Prop.Obelisk: radius = 0.8f; height = 8.0f; return;
                case Prop.Windmill: radius = 2.3f; height = 9.0f; return;
                case Prop.Wagon: radius = 1.25f; height = 1.0f; return;
                default: radius = 0f; height = 0f; return;
            }
        }

        //                                          Tuft Peb  RTan RDrk RRed RWht BTan BDrk BRed Slab Cact Barr Bush Tree Bone Colm Stel Cairn | Tent FireP Crate Well Obel Wind Wagon
        //                                          (the tent has no density of its own: it comes with a fire, a camp; the fire, the well, the obelisk, the windmill and the wagon are rare, one every few squares)
        static readonly float[][] Density =
        {
            new[] { 5.0f, 4.0f, 4.0f, 1.0f, 0.5f, 0.0f, 0.5f, 0.1f, 0.05f, 0.1f, 1.4f, 1.0f, 1.6f, 0.45f, 0.3f, 0.05f, 0.03f, 0.10f,   0f, 0.014f, 0.030f, 0.002f, 0.004f, 0.003f, 0.012f },      // dunes
            new[] { 7.0f, 4.0f, 3.0f, 1.5f, 0.0f, 0.0f, 0.25f, 0.15f, 0.0f, 0.0f, 0.5f, 0.4f, 2.6f, 0.80f, 0.15f, 0.05f, 0.05f, 0.12f,   0f, 0.012f, 0.050f, 0.012f, 0.003f, 0.012f, 0.018f },     // hamlets
            new[] { 3.0f, 4.0f, 2.0f, 0.3f, 0.0f, 2.0f, 0.3f, 0.0f, 0.0f, 0.1f, 0.2f, 0.1f, 0.9f, 0.35f, 0.4f, 0.70f, 0.45f, 0.20f,   0f, 0.010f, 0.020f, 0.004f, 0.014f, 0.000f, 0.006f },       // holy land
            new[] { 3.0f, 5.0f, 2.0f, 3.0f, 0.0f, 0.5f, 0.1f, 0.5f, 0.0f, 0.1f, 0.25f, 0.1f, 0.8f, 0.50f, 0.35f, 0.40f, 0.20f, 0.10f,   0f, 0.018f, 0.060f, 0.004f, 0.009f, 0.003f, 0.030f },      // dead town
            new[] { 1.5f, 6.0f, 2.0f, 2.0f, 6.0f, 0.0f, 0.4f, 0.5f, 2.4f, 1.6f, 0.7f, 0.6f, 0.4f, 0.20f, 0.4f, 0.05f, 0.05f, 0.45f,   0f, 0.012f, 0.020f, 0.000f, 0.006f, 0.000f, 0.020f },       // badlands
            new[] { 0.4f, 3.0f, 0.5f, 0.2f, 0.0f, 2.5f, 0.0f, 0.0f, 0.0f, 0.1f, 0.0f, 0.0f, 0.1f, 0.25f, 1.4f, 0.10f, 0.10f, 0.25f,   0f, 0.010f, 0.010f, 0.000f, 0.008f, 0.000f, 0.010f },       // salt flats
        };
        static readonly float[] MaxSlope = { 0.6f, 0.7f, 1.2f, 1.2f, 1.2f, 1.2f, 1.4f, 1.4f, 1.4f, 1.2f, 0.5f, 0.6f, 0.6f, 0.5f, 0.45f, 0.35f, 0.35f, 0.6f,   0.35f, 0.3f, 0.5f, 0.3f, 0.3f, 0.3f, 0.3f };
        static readonly float[] MinScale = { 0.8f, 0.8f, 0.6f, 0.6f, 0.6f, 0.6f, 0.9f, 0.9f, 0.9f, 0.8f, 0.7f, 0.7f, 0.7f, 0.7f, 0.8f, 0.8f, 0.8f, 0.7f,   0.95f, 0.95f, 0.85f, 0.95f, 0.9f, 0.95f, 0.9f };
        static readonly float[] MaxScale = { 1.5f, 1.5f, 1.6f, 1.6f, 1.6f, 1.6f, 2.3f, 2.3f, 2.3f, 1.6f, 1.5f, 1.4f, 1.5f, 1.4f, 1.3f, 1.2f, 1.2f, 1.4f,   1.1f, 1.05f, 1.2f, 1.1f, 1.5f, 1.2f, 1.15f };
        /// <summary>How deep into the sand each prop sits (m at scale 1): rocks are half buried, grass is not.</summary>
        static readonly float[] Sink = { 0f, 0.02f, 0.18f, 0.18f, 0.18f, 0.18f, 0.35f, 0.35f, 0.35f, 0.3f, 0.05f, 0.05f, 0.03f, 0.1f, 0.02f, 0.12f, 0.1f, 0.04f,   0.02f, 0.02f, 0.08f, 0.15f, 0.3f, 0.5f, 0.1f };

        sealed class Foot { public float x0, z0, x1, z1; }

        /// <summary>All the props of the 64 m square (cx, cz).</summary>
        public static void Build(int cx, int cz, List<PropItem> into)
        {
            into.Clear();
            float ox = cx * Chunk, oz = cz * Chunk;
            DesertWorld.RegionBlend(ox + Chunk * 0.5f, oz + Chunk * 0.5f, out var ra, out var rb, out float wa);
            var rng = new Rng(unchecked(cx * 73856093 ^ cz * 19349663 ^ (DesertWorld.Seed + 101) * 83492791));
            // the footprints that can touch this square, or the groups that spill out of it (a colonnade reaches 16 m past the square's edge)
            var feet = new List<Foot>();
            for (int i = DesertWorld.CellOf(ox - Leak) - 1; i <= DesertWorld.CellOf(ox + Chunk + Leak) + 1; i++)
                for (int j = DesertWorld.CellOf(oz - Leak) - 1; j <= DesertWorld.CellOf(oz + Chunk + Leak) + 1; j++)
                {
                    var s = DesertWorld.SiteOf(i, j);
                    if (s == null) continue;
                    const float m = 7f;
                    if (s.fx1 + m < ox - Leak || s.fx0 - m > ox + Chunk + Leak || s.fz1 + m < oz - Leak || s.fz0 - m > oz + Chunk + Leak) continue;
                    feet.Add(new Foot { x0 = s.fx0 - m, z0 = s.fz0 - m, x1 = s.fx1 + m, z1 = s.fz1 + m });
                }
            for (int k = 0; k < (int)Prop.Count; k++)
            {
                float lam = Density[(int)rb][k] + (Density[(int)ra][k] - Density[(int)rb][k]) * wa;
                if (lam <= 0f) continue;
                int n = (int)lam;
                if (rng.Value() < lam - n) n++;
                for (int i = 0; i < n; i++) Place((Prop)k, ox, oz, rng, feet, into);
            }
        }

        static bool Free(float x, float z, List<Foot> feet)
        {
            if (DesertWorld.DistToCathedral(x, z) < 14f) return false;
            foreach (var f in feet) if (x > f.x0 && x < f.x1 && z > f.z0 && z < f.z1) return false;
            return true;
        }

        static bool Add(Prop p, float x, float z, Rng rng, List<Foot> feet, List<PropItem> into, float scaleMul = 1f, float yawDeg = float.NaN)
        {
            int k = (int)p;
            if (!Free(x, z, feet)) return false;
            float y = DesertWorld.Height(x, z);
            const float e = 1.2f;
            float sx = Math.Abs(DesertWorld.Height(x + e, z) - DesertWorld.Height(x - e, z)) / (2f * e), sz = Math.Abs(DesertWorld.Height(x, z + e) - DesertWorld.Height(x, z - e)) / (2f * e);
            if (Math.Max(sx, sz) > MaxSlope[k]) return false;
            float scale = rng.Range(MinScale[k], MaxScale[k]) * scaleMul;
            float yaw = rng.Range(0f, 360f);                    // always drawn, so that a given yaw does not shift the random numbers that follow
            if (!float.IsNaN(yawDeg)) yaw = yawDeg;
            into.Add(new PropItem { kind = (byte)k, x = x, y = y - Sink[k] * scale, z = z, yaw = yaw, scale = scale });
            return true;
        }

        /// <summary>A camp round a fire at (x, z): one to three tents 4 to 5.4 m from it with their doors to the flames, a pile of supplies on the other side, a few bones and a barrel.</summary>
        static void Camp(float x, float z, Rng rng, List<Foot> feet, List<PropItem> into)
        {
            int tents = rng.Range(1, 4);
            float a0 = rng.Range(0f, 6.283f);
            for (int i = 0; i < tents; i++)
            {
                float a = a0 + i * 6.283f / tents + rng.Range(-0.35f, 0.35f), d = rng.Range(4.0f, 5.4f);
                Add(Prop.Tent, x + (float)Math.Cos(a) * d, z + (float)Math.Sin(a) * d, rng, feet, into, 1f, -a * 57.29578f);       // the door (local -x) looks at the fire
            }
            float c = a0 + 3.4f;
            Add(Prop.Crate, x + (float)Math.Cos(c) * 3.0f, z + (float)Math.Sin(c) * 3.0f, rng, feet, into);
            if (rng.Value() < 0.6f) Add(Prop.Barrel, x + (float)Math.Cos(c + 0.9f) * 3.4f, z + (float)Math.Sin(c + 0.9f) * 3.4f, rng, feet, into);
            if (rng.Value() < 0.5f) Add(Prop.Bones, x + (float)Math.Cos(c - 1.0f) * 6.5f, z + (float)Math.Sin(c - 1.0f) * 6.5f, rng, feet, into);
        }

        static void Place(Prop p, float ox, float oz, Rng rng, List<Foot> feet, List<PropItem> into)
        {
            float x = ox + rng.Range(0f, Chunk), z = oz + rng.Range(0f, Chunk);
            if (!Add(p, x, z, rng, feet, into)) return;
            if (p == Prop.FirePit) { Camp(x, z, rng, feet, into); return; }
            // some props come in groups: a patch of grass, a handful of pebbles, a colonnade, a few bones, two or three steles
            int more = 0; float spread = 0f; bool line = false;
            switch (p)
            {
                case Prop.Tuft: more = rng.Range(2, 6); spread = 1.8f; break;
                case Prop.Pebbles: more = rng.Range(2, 5); spread = 1.3f; break;
                case Prop.Bones: more = rng.Range(0, 3); spread = 2.5f; break;
                case Prop.Column: more = rng.Range(1, 5); spread = 3.4f; line = true; break;
                case Prop.Stele: more = rng.Range(0, 3); spread = 3.0f; line = true; break;
                case Prop.BoulderTan: case Prop.BoulderDark: case Prop.BoulderRed: more = rng.Range(0, 3); spread = 4.5f; break;
                case Prop.Bush: more = rng.Range(0, 3); spread = 3f; break;
            }
            float dir = rng.Range(0f, 6.283f);
            for (int i = 1; i <= more; i++)
            {
                float a = line ? dir + rng.Range(-0.15f, 0.15f) : rng.Range(0f, 6.283f);
                float d = line ? spread * i * rng.Range(0.9f, 1.15f) : rng.Range(0.3f, 1f) * spread;
                Add(p, x + (float)Math.Cos(a) * d, z + (float)Math.Sin(a) * d, rng, feet, into, line ? 1f - 0.1f * i : 1f);
            }
        }
    }
}
