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
        Tent, FirePit, Crate, Well, Obelisk, Windmill, Wagon,
        Broadleaf, Fir, FrostFir, MeadowGrass, Wildflowers, GlacierShard, ForestFern, Count
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
                case Prop.Tuft: case Prop.Pebbles: case Prop.Bones: case Prop.Cairn: case Prop.Barrel: case Prop.Crate:
                case Prop.MeadowGrass: case Prop.Wildflowers: case Prop.ForestFern: return 0;
                case Prop.RockTan: case Prop.RockDark: case Prop.RockRed: case Prop.RockWhite: case Prop.Cactus: case Prop.Bush:
                case Prop.Tent: case Prop.FirePit: case Prop.Well: case Prop.Wagon:
                case Prop.Broadleaf: case Prop.Fir: case Prop.FrostFir: return 1;
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
                case Prop.Broadleaf: radius = 0.35f; height = 4.5f; return;
                case Prop.Fir: case Prop.FrostFir: radius = 0.30f; height = 5.2f; return;
                case Prop.GlacierShard: radius = 0.53f; height = 2.3f; return;
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
        static readonly float[] MaxSlope = { 0.6f, 0.7f, 1.2f, 1.2f, 1.2f, 1.2f, 1.4f, 1.4f, 1.4f, 1.2f, 0.5f, 0.6f, 0.6f, 0.5f, 0.45f, 0.35f, 0.35f, 0.6f,   0.35f, 0.3f, 0.5f, 0.3f, 0.3f, 0.3f, 0.3f,   0.64f, 0.68f, 0.48f, 0.74f, 0.56f, 0.88f, 0.63f };
        static readonly float[] MinScale = { 0.8f, 0.8f, 0.6f, 0.6f, 0.6f, 0.6f, 0.9f, 0.9f, 0.9f, 0.8f, 0.7f, 0.7f, 0.7f, 0.7f, 0.8f, 0.8f, 0.8f, 0.7f,   0.95f, 0.95f, 0.85f, 0.95f, 0.9f, 0.95f, 0.9f,   0.68f, 0.66f, 0.54f, 0.74f, 0.58f, 0.52f, 0.60f };
        static readonly float[] MaxScale = { 1.5f, 1.5f, 1.6f, 1.6f, 1.6f, 1.6f, 2.3f, 2.3f, 2.3f, 1.6f, 1.5f, 1.4f, 1.5f, 1.4f, 1.3f, 1.2f, 1.2f, 1.4f,   1.1f, 1.05f, 1.2f, 1.1f, 1.5f, 1.2f, 1.15f,   1.24f, 1.20f, 0.98f, 1.6f, 1.44f, 1.55f, 1.55f };
        /// <summary>How deep into the sand each prop sits (m at scale 1): rocks are half buried, grass is not.</summary>
        static readonly float[] Sink = { 0f, 0.02f, 0.18f, 0.18f, 0.18f, 0.18f, 0.35f, 0.35f, 0.35f, 0.3f, 0.05f, 0.05f, 0.03f, 0.1f, 0.02f, 0.12f, 0.1f, 0.04f,   0.02f, 0.02f, 0.08f, 0.15f, 0.3f, 0.5f, 0.1f,   0.13f, 0.13f, 0.16f, 0.06f, 0.04f, 0.24f, 0.045f };

        sealed class Foot { public float x0, z0, x1, z1; }

        /// <summary>All the props of the 64 m square (cx, cz).</summary>
        public static void Build(int cx, int cz, List<PropItem> into)
        {
            into.Clear();
            float ox = cx * Chunk, oz = cz * Chunk;
            DesertWorld.RegionBlend(ox + Chunk * 0.5f, oz + Chunk * 0.5f, out var ra, out var rb, out float wa);
            var rng = new Rng(unchecked(cx * 73856093 ^ cz * 19349663 ^ (DesertWorld.Seed + 101) * 83492791));
            // the footprints that can touch this square, or the groups that spill out of it (a colonnade reaches 16 m past the square's edge)
            var feet = new List<Foot>(); var bare = new List<Foot>(); var near = new List<Site>();
            for (int i = DesertWorld.CellOf(ox - Leak) - 1; i <= DesertWorld.CellOf(ox + Chunk + Leak) + 1; i++)
                for (int j = DesertWorld.CellOf(oz - Leak) - 1; j <= DesertWorld.CellOf(oz + Chunk + Leak) + 1; j++)
                {
                    var s = DesertWorld.SiteOf(i, j);
                    if (s == null) continue;
                    const float m = 7f;
                    if (s.fx1 + m < ox - Leak || s.fx0 - m > ox + Chunk + Leak || s.fz1 + m < oz - Leak || s.fz0 - m > oz + Chunk + Leak) continue;
                    feet.Add(new Foot { x0 = s.fx0 - m, z0 = s.fz0 - m, x1 = s.fx1 + m, z1 = s.fz1 + m });
                    bare.Add(new Foot { x0 = s.fx0 - 0.9f, z0 = s.fz0 - 0.9f, x1 = s.fx1 + 0.9f, z1 = s.fz1 + 0.9f });
                    near.Add(s);
                }
            var centreClimate = DesertBiomes.At(ox + Chunk * 0.5f, oz + Chunk * 0.5f, DesertWorld.Seed);
            for (int k = 0; k < (int)Prop.Broadleaf; k++)
            {
                float lam = Density[(int)rb][k] + (Density[(int)ra][k] - Density[(int)rb][k]) * wa;
                lam *= centreClimate.Desert;
                if (lam <= 0f) continue;
                int n = (int)lam;
                if (rng.Value() < lam - n) n++;
                for (int i = 0; i < n; i++) Place((Prop)k, ox, oz, rng, feet, into);
            }
            if (centreClimate.Desert < 1f || DesertBiomes.At(ox, oz, DesertWorld.Seed).Desert < 1f ||
                DesertBiomes.At(ox + Chunk, oz, DesertWorld.Seed).Desert < 1f ||
                DesertBiomes.At(ox, oz + Chunk, DesertWorld.Seed).Desert < 1f ||
                DesertBiomes.At(ox + Chunk, oz + Chunk, DesertWorld.Seed).Desert < 1f)
                AddBiomeVegetation(ox, oz, rng, feet, into);
            // the yards of the buildings near this square: each square keeps the props that fall inside it
            var yard = new List<YardItem>();
            foreach (var s in near)
            {
                Yard(s, yard);
                for (int n = 0; n < yard.Count; n++)
                    if (yard[n].x >= ox && yard[n].x < ox + Chunk && yard[n].z >= oz && yard[n].z < oz + Chunk) AddYard(yard[n], s, n, bare, into);
            }
        }

        static void AddBiomeVegetation(float ox, float oz, Rng rng, List<Foot> feet, List<PropItem> into)
        {
            // Jittered cells keep forest trunks apart. A second, finer grid gives grass a different scale.
            for (int z = 0; z < 8; z++)
                for (int x = 0; x < 8; x++)
                {
                    float px = ox + (x + rng.Range(0.13f, 0.87f)) * 8f;
                    float pz = oz + (z + rng.Range(0.13f, 0.87f)) * 8f;
                    var biome = DesertBiomes.At(px, pz, DesertWorld.Seed);
                    float glade = DesertNoise.Smooth(0.29f, 0.64f, DesertNoise.Value(px / 48f, pz / 52f, DesertWorld.Seed + 9234));
                    float trees = biome.Forest * (0.37f + glade * 0.43f) + biome.Meadow * 0.028f;
                    float roll = (float)rng.Value();
                    if (roll < trees)
                    {
                        Add(rng.Value() < 0.29 ? Prop.Fir : Prop.Broadleaf, px, pz, rng, feet, into);
                    }
                    else if (roll < trees + biome.Ice * (0.09f + glade * 0.15f))
                    {
                        float pack = DesertBiomes.IceSheet(px, pz, DesertWorld.Seed, biome);
                        Add(rng.Value() < 0.55 - pack * 0.44 ? Prop.FrostFir : Prop.GlacierShard, px, pz, rng, feet, into);
                    }
                }
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    float px = ox + (x + rng.Range(0.02f, 0.98f)) * 4f;
                    float pz = oz + (z + rng.Range(0.02f, 0.98f)) * 4f;
                    var biome = DesertBiomes.At(px, pz, DesertWorld.Seed);
                    float growth = biome.Meadow * 0.57f + biome.Forest * 0.21f;
                    float undergrowth = biome.Forest * 0.32f * DesertNoise.Smooth(0.21f, 0.63f, DesertNoise.Value(px / 27f, pz / 22f, DesertWorld.Seed + 9254));
                    float roll = (float)rng.Value();
                    if (roll >= growth + undergrowth) continue;
                    if (roll >= growth)
                    {
                        Add(Prop.ForestFern, px, pz, rng, feet, into);
                        continue;
                    }
                    bool flowers = rng.Value() < 0.18f + biome.Meadow * 0.16f;
                    Add(flowers ? Prop.Wildflowers : Prop.MeadowGrass, px, pz, rng, feet, into);
                }
        }

        /// <summary>One prop of a yard: what and where. The rest (height, size, turn) is drawn by the square that holds it.</summary>
        public struct YardItem { public Prop kind; public float x, z; }

        /// <summary>The points (m) where a building opens to the outside on the ground: its doors, not its windows.</summary>
        public static void Doors(Site s, List<(float x, float z)> into)
        {
            into.Clear();
            var p = s.plan;
            if (p == null) return;
            foreach (var o in p.openings)
                if (o.b < 0 && o.sill < 0.01f && !o.oneSided) into.Add(o.alongX ? (o.center * 0.1f, o.line * 0.1f) : (o.line * 0.1f, o.center * 0.1f));
        }

        /// <summary>The yard of a building: the sand by its walls is not bare. Three to seven props (two to four for a house or a chapel) 1.4 to 4 m outside the footprint, by the walls: crates and
        /// barrels, a wagon now and then, a dead tree or a bush, tufts, bones, and by a church steles, cairns and a broken column. None within 4.2 m of a door that opens to the outside (a walker has
        /// to get in). A pure function of the place's own seed: every 64 m square that the yard touches asks for the same list and keeps the props that fall in it.</summary>
        public static void Yard(Site s, List<YardItem> into)
        {
            into.Clear();
            if (s.plan == null || s.IsTower || s.IsCatacomb) return;
            var rng = new Rng(unchecked(s.seed * 1103515245 ^ 0x2F6B1D3));
            var doors = new List<(float x, float z)>();
            Doors(s, doors);
            float w = s.fx1 - s.fx0, d = s.fz1 - s.fz0;
            int target = s.kind == SiteKind.Casa || s.kind == SiteKind.Cappella ? rng.Range(2, 5) : rng.Range(3, 8);
            for (int tries = 0; tries < 48 && into.Count < target; tries++)
            {
                int side = rng.Range(0, 4);
                float t = (float)rng.Value(), off = rng.Range(1.4f, 4.0f);
                var kind = PickYard(s, rng);
                float x, z;
                switch (side)
                {
                    case 0: x = s.fx0 + t * w; z = s.fz0 - off; break;
                    case 1: x = s.fx0 + t * w; z = s.fz1 + off; break;
                    case 2: x = s.fx0 - off; z = s.fz0 + t * d; break;
                    default: x = s.fx1 + off; z = s.fz0 + t * d; break;
                }
                bool atADoor = false;
                foreach (var dr in doors) { float dx = x - dr.x, dz = z - dr.z; if (dx * dx + dz * dz < 4.2f * 4.2f) { atADoor = true; break; } }
                if (!atADoor) into.Add(new YardItem { kind = kind, x = x, z = z });
            }
        }

        static Prop PickYard(Site s, Rng rng)
        {
            float r = (float)rng.Value(), r2 = (float)rng.Value();          // always drawn: the list must not depend on the branch taken
            bool worn = s.state != BuildingState.Intact;
            Prop k;
            switch (s.kind)
            {
                case SiteKind.Chiesa: case SiteKind.Cappella:
                    k = r < 0.28f ? Prop.Stele : r < 0.50f ? Prop.Cairn : r < 0.68f ? Prop.Bones : r < 0.82f ? Prop.DeadTree : r < 0.92f ? Prop.Column : Prop.Bush; break;
                case SiteKind.Casa: case SiteKind.Locanda:
                    k = r < 0.24f ? Prop.Barrel : r < 0.46f ? Prop.Crate : r < 0.60f ? Prop.Bush : r < 0.72f ? Prop.DeadTree : r < 0.84f ? Prop.Tuft : r < 0.92f ? Prop.Bones : Prop.Wagon; break;
                default:
                    k = r < 0.22f ? Prop.Crate : r < 0.40f ? Prop.Barrel : r < 0.52f ? Prop.Wagon : r < 0.66f ? Prop.DeadTree : r < 0.78f ? Prop.Bush : r < 0.90f ? Prop.Tuft : Prop.Bones; break;
            }
            if (worn && (k == Prop.Crate || k == Prop.Barrel) && r2 < 0.35f) k = Prop.Bones;           // in a ruin the stores have long been looted
            return k;
        }

        static void AddYard(YardItem y, Site s, int n, List<Foot> bare, List<PropItem> into)
        {
            var biome = DesertBiomes.At(y.x, y.z, DesertWorld.Seed).Dominant;
            if (y.kind == Prop.Tuft || y.kind == Prop.Bush)
            {
                if (biome == Biome.Ice) return;
                if (biome != Biome.Desert) y.kind = Prop.MeadowGrass;
            }
            int k = (int)y.kind;
            if (DesertWorld.DistToCathedral(y.x, y.z) < 14f) return;
            foreach (var f in bare) if (y.x > f.x0 && y.x < f.x1 && y.z > f.z0 && y.z < f.z1) return;            // never on another place
            var rng = new Rng(unchecked(s.seed * 40503 + (n + 1) * 2654435));
            float scale = rng.Range(MinScale[k], MaxScale[k]), yaw = rng.Range(0f, 360f);
            const float e = 1.2f;
            float sx = Math.Abs(DesertWorld.Height(y.x + e, y.z) - DesertWorld.Height(y.x - e, y.z)) / (2f * e), sz = Math.Abs(DesertWorld.Height(y.x, y.z + e) - DesertWorld.Height(y.x, y.z - e)) / (2f * e);
            if (Math.Max(sx, sz) > MaxSlope[k] + 0.25f) return;             // the sand by a wall was levelled for the building, but its ramps can be steep
            into.Add(new PropItem { kind = (byte)k, x = y.x, y = DesertWorld.Height(y.x, y.z) - Sink[k] * scale, z = y.z, yaw = yaw, scale = scale });
        }

        static bool Free(float x, float z, List<Foot> feet)
        {
            if (DesertWorld.DistToCathedral(x, z) < 14f || DesertWorld.LakeAt(x, z, out _, out _)) return false;
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
