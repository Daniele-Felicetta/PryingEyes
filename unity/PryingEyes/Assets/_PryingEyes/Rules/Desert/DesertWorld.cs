using System;
using System.Collections.Generic;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    public enum SiteKind { Casa, Cappella, Locanda, Chiesa, Albergo, Teatro, Sanatorio, Complesso, Torre, Catacomba }

    /// <summary>The character of a stretch of desert (about a kilometre across): which places stand there, how tall the dunes are, what colour the sand is, what lies on it.
    /// The cathedral's own region is always the Hamlets, so that the first look from the steps already holds houses.</summary>
    public enum Region { Dunes, Hamlets, Holy, DeadTown, Badlands, Salt }

    /// <summary>A place of the desert: a building (whole, ruined, split or collapsing), a tower of many floors or the mausoleum that leads down to the
    /// catacombs. Everything about it is decided by the world seed and its cell, so it is the same every time the player comes back.</summary>
    public sealed class Site
    {
        public int cx, cz, seed;
        public SiteKind kind;
        public BuildingState state;
        public Region region;
        public int palette;                 // which of the wall colours of its theme (FacadeKit), 0..63
        public bool forced;                 // the welcome chapel and the landmark tower stand where the design put them
        public float jx, jz;                // where in the free part of its cell the footprint lies (0..1)
        public float x, z;                  // centre (m)
        public float padY;                  // the level ground is flattened to under it (m); the entrance level's floor is at this height
        public float fx0, fz0, fx1, fz1;    // footprint (m)
        public Plan plan;                   // a building: in world decimetres
        public Plan[] levels;               // a tower or a catacomb: level 0 is the lowest one
        public int entrance, boss;          // which level is the door on, which holds the boss
        public Theme theme;
        public bool church;
        /// <summary>Where the stack's local coordinates (dm) were moved to land on the site: the stair room of every level sits at (stackDx, stackDz).</summary>
        public int stackDx, stackDz;

        public bool IsStackKind => kind == SiteKind.Torre || kind == SiteKind.Catacomba;
        public bool IsTower => kind == SiteKind.Torre;
        public bool IsCatacomb => kind == SiteKind.Catacomba;
        public bool IsStack => levels != null;
        public int LevelCount => levels == null ? 0 : levels.Length;

        /// <summary>The y of the floor of a stack level: a tower goes up from its door, the catacombs go down from it.</summary>
        public float LevelY(int level) => padY + (level - entrance) * PlanGen.LevelHeight;
        public float Height => IsTower ? LevelCount * PlanGen.LevelHeight : 6f;

        /// <summary>The opening in the sand over the stair room of a catacomb (m): the stairs come up through it to the entrance level, so the ground
        /// must not cover it. The rectangle is the inside of the room, the walls stand on its edge. False for everything else.</summary>
        public bool WellRect(out HoleRect r)
        {
            r = default;
            if (!IsCatacomb || levels == null) return false;
            var stair = levels[entrance].rooms[0];
            const float wall = 0.15f;
            r = new HoleRect(stair.x0 * 0.1f + wall, stair.z0 * 0.1f + wall, stair.x1 * 0.1f - wall, stair.z1 * 0.1f - wall);
            return true;
        }
        public float CentreX => (fx0 + fx1) * 0.5f;
        public float CentreZ => (fz0 + fz1) * 0.5f;
        public override string ToString() => $"{kind}/{state} cell({cx},{cz}) at ({x:F0},{z:F0})" + (IsStack ? $" {LevelCount} levels" : plan != null ? $" {plan.rooms.Count} rooms" : "");
    }

    /// <summary>Lake shape shared by terrain, map, surface mesh and player movement.</summary>
    public readonly struct DesertLake
    {
        public const float Depth = 8.2f;
        const float ShelfDepth = 3.5f;
        public const float BankRadius = 1.36f;
        public const float OutlineExtent = 1.24f;
        public readonly float X, Z, RadiusX, RadiusZ, Surface, ShoreTurn;

        public DesertLake(float x, float z, float radiusX, float radiusZ, float surface, float shoreTurn = 0f)
        {
            X = x;
            Z = z;
            RadiusX = radiusX;
            RadiusZ = radiusZ;
            Surface = surface;
            ShoreTurn = shoreTurn;
        }

        float Outline(float angle)
        {
            angle += ShoreTurn;
            return 1f + 0.132f * (float)Math.Sin(3f * angle + 0.7f) + 0.065f * (float)Math.Cos(2f * angle + 0.2f) +
                0.038f * (float)Math.Cos(5f * angle - 0.45f);
        }

        public float RadiusAt(float x, float z)
        {
            float dx = (x - X) / RadiusX, dz = (z - Z) / RadiusZ;
            float squared = dx * dx + dz * dz;
            const float outer = BankRadius * OutlineExtent;
            if (squared > outer * outer || squared == 0f) return (float)Math.Sqrt(squared);
            return (float)Math.Sqrt(squared) / Outline((float)Math.Atan2(dz, dx));
        }

        public (float x, float z) PointAt(float angle, float radius)
        {
            float edge = radius * Outline(angle);
            return (X + RadiusX * edge * (float)Math.Cos(angle), Z + RadiusZ * edge * (float)Math.Sin(angle));
        }

        public static float BedDepth(float radius) =>
            ShelfDepth * (1f - radius * radius) + (Depth - ShelfDepth) * (1f - DesertNoise.Smooth(0.13f, 0.80f, radius));
    }

    /// <summary>Seeded ground, sites and lakes shared by terrain chunks, buildings and the navmesh window.</summary>
    public static class DesertWorld
    {
        public static int Seed = 1;

        /// <summary>The sand level next to the cathedral (m): the stairs of the churchyard go down to it from the cathedral's floor (y = 0).</summary>
        public const float GroundY = -2.4f;
        /// <summary>The cathedral's footprint (m): the rooms of HubPlan, with the armoury that the desert adds on the east side (up to x = 47).</summary>
        public const float CathX0 = 0f, CathZ0 = 0f, CathX1 = 47f, CathZ1 = 66f;
        /// <summary>One cell holds at most one site. A site is at most 110 m wide (PlanGen.BuildingSpan) and keeps CellMargin from the edges of its cell,
        /// so two neighbours are never closer than twice the margin.</summary>
        public const float Cell = 140f;
        public const float CellMargin = 12.5f;
        /// <summary>The flat apron around a footprint, and how far the sand is calmed down beyond it (the dunes between two places stay).</summary>
        public const float PadMargin = 4f, PadBlend = 6f, CalmRadius = 40f, CalmStrength = 0.62f;
        /// <summary>Where the welcome chapel (a ruin near the stairs, always) and the landmark tower (on the horizon, always) stand.</summary>
        public const float WelcomeX = 100f, WelcomeZ = -100f, LandmarkX = 100f, LandmarkZ = -350f;
        public static int CellOf(float v) => (int)Math.Floor(v / Cell);
        public static Site Welcome() => SiteOf(CellOf(WelcomeX), CellOf(WelcomeZ));
        public static Site Landmark() => SiteOf(CellOf(LandmarkX), CellOf(LandmarkZ));
        /// <summary>Where the desert ends: beyond it floats lose precision, so the director turns the walker back (<c>DesertDirector.EdgeTick</c>).</summary>
        public const float WorldRadius = 18000f;

        static readonly Dictionary<long, Site> cache = new Dictionary<long, Site>();
        /// <summary>The terrain is computed on worker threads and asks for the sites around each point: the cache is shared, so every access is locked.
        /// The director makes the sites of a chunk's cells on the main thread before the chunk is queued, so a worker normally only finds them ready.</summary>
        static readonly object gate = new object();
        static IReadOnlyList<DesertLake> lakes = Array.Empty<DesertLake>();
        public static IReadOnlyList<DesertLake> Lakes => lakes;

        public static void Reset(int seed)
        {
            lock (gate)
            {
                Seed = seed;
                cache.Clear();
                originFor = int.MinValue;
                lakes = FindLakes().AsReadOnly();
            }
        }

        static List<DesertLake> FindLakes()
        {
            var result = new List<DesertLake>(2);
            // Search two separate sectors near the cathedral. Existing sites and their approaches keep their original ground.
            for (int sector = 0; sector < 2; sector++)
            {
                bool found = false;
                for (int row = 0; row < 9 && !found; row++)
                    for (int col = 0; col < 9 && !found; col++)
                    {
                        float across = 22f * ((col + 1) / 2) * (col % 2 == 0 ? -1f : 1f);
                        float x = sector == 0 ? -84f - row * 30f : 28f + across;
                        float z = sector == 0 ? 32f + across : 150f + row * 30f;
                        float rx = sector == 0 ? 25f : 20f, rz = sector == 0 ? 18f : 26f;
                        float reachX = rx * DesertLake.OutlineExtent * DesertLake.BankRadius + 10f;
                        float reachZ = rz * DesertLake.OutlineExtent * DesertLake.BankRadius + 10f;
                        if ((sector == 1 && x < 0f) || !LakeSiteClear(x, z, reachX, reachZ)) continue;
                        float surface = DryHeight(x, z) - 0.65f;
                        float turn = sector * 1.12f + (DesertNoise.Hash01(sector, 0, Seed + 9250) - 0.5f) * 0.74f;
                        result.Add(new DesertLake(x, z, rx, rz, surface, turn));
                        found = true;
                    }
            }
            return result;
        }

        static bool LakeSiteClear(float x, float z, float reachX, float reachZ)
        {
            for (int cx = CellOf(x - reachX); cx <= CellOf(x + reachX); cx++)
                for (int cz = CellOf(z - reachZ); cz <= CellOf(z + reachZ); cz++)
                {
                    var site = SiteOf(cx, cz);
                    if (site != null && site.fx0 < x + reachX && site.fx1 > x - reachX &&
                        site.fz0 < z + reachZ && site.fz1 > z - reachZ) return false;
                }
            return true;
        }

        public static bool LakeAt(float x, float z, out DesertLake lake, out float depth)
        {
            var current = lakes;
            for (int i = 0; i < current.Count; i++)
            {
                lake = current[i];
                float radius = lake.RadiusAt(x, z);
                if (radius >= 1f) continue;
                depth = DesertLake.BedDepth(radius);
                return true;
            }
            lake = default;
            depth = 0f;
            return false;
        }

        // ------------------------------------------------------------------ the regions
        /// <summary>A region is the Voronoi cell of a random point in every square of this size (m): about a kilometre across, so a walk of ten minutes crosses one.</summary>
        public const float RegionCell = 1000f;
        static int originI, originJ, originFor = int.MinValue;

        // what each region is like: how many cells hold a site, the dune height, the tint of the sand (multiplies the shader's own colours), the kinds and the states of its places
        static readonly float[] RegDensity = { 0.50f, 0.92f, 0.62f, 0.80f, 0.38f, 0.28f };
        static readonly float[] RegAmp = { 1.25f, 0.85f, 0.95f, 0.80f, 1.50f, 0.25f };
        static readonly float[][] RegTint =
        {
            new[] { 1.00f, 1.00f, 1.00f },       // dunes: the sand of the shader
            new[] { 0.93f, 0.92f, 0.82f },       // hamlets: dry earth
            new[] { 1.10f, 1.08f, 1.04f },       // holy land: pale, almost chalk
            new[] { 0.78f, 0.79f, 0.82f },       // dead town: ash
            new[] { 1.10f, 0.80f, 0.64f },       // badlands: red (terracotta: 1.14 / 0.76 / 0.60 was a Martian orange, and at dusk a crimson)
            new[] { 1.22f, 1.22f, 1.18f },       // salt flats: white
        };
        //                                            Casa Cappella Locanda Chiesa Albergo Teatro Sanatorio Complesso Torre Catacomba
        static readonly double[][] RegKinds =
        {
            new double[] { 16, 12, 8, 6, 5, 3, 3, 4, 14, 16 },         // dunes
            new double[] { 40, 14, 22, 5, 3, 1, 1, 4, 5, 4 },          // hamlets
            new double[] { 6, 22, 3, 24, 2, 2, 2, 6, 14, 28 },         // holy land
            new double[] { 8, 4, 10, 4, 16, 14, 14, 12, 10, 6 },       // dead town
            new double[] { 10, 8, 4, 4, 2, 2, 2, 8, 24, 36 },          // badlands
            new double[] { 6, 6, 2, 4, 0.1, 0.1, 0.1, 2, 16, 20 },     // salt flats
        };
        //                                            whole ruin split collapsing
        static readonly double[][] RegStates =
        {
            new double[] { 30, 26, 22, 22 },
            new double[] { 42, 28, 16, 14 },
            new double[] { 34, 26, 22, 18 },
            new double[] { 16, 24, 24, 36 },
            new double[] { 24, 30, 20, 26 },
            new double[] { 26, 30, 20, 24 },
        };
        static readonly float[] RegCum = { 0.26f, 0.43f, 0.58f, 0.73f, 0.90f, 1.0f };

        static void FeaturePoint(int i, int j, out float px, out float pz)
        {
            px = (i + 0.15f + 0.7f * DesertNoise.Hash01(i, j, Seed + 71)) * RegionCell;
            pz = (j + 0.15f + 0.7f * DesertNoise.Hash01(i, j, Seed + 72)) * RegionCell;
        }

        /// <summary>The two nearest feature points to (x, z): their cells and distances.</summary>
        static void Nearest2(float x, float z, out int i1, out int j1, out float d1, out int i2, out int j2, out float d2)
        {
            int i0 = (int)Math.Floor(x / RegionCell), j0 = (int)Math.Floor(z / RegionCell);
            i1 = j1 = i2 = j2 = 0; d1 = d2 = float.MaxValue;
            for (int i = i0 - 1; i <= i0 + 1; i++)
                for (int j = j0 - 1; j <= j0 + 1; j++)
                {
                    FeaturePoint(i, j, out float px, out float pz);
                    float dx = px - x, dz = pz - z, d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d < d1) { i2 = i1; j2 = j1; d2 = d1; i1 = i; j1 = j; d1 = d; }
                    else if (d < d2) { i2 = i; j2 = j; d2 = d; }
                }
        }

        static Region RegionOfCell(int i, int j)
        {
            if (originFor != Seed)
            {
                lock (gate)
                {
                    Nearest2(CathX0 + (CathX1 - CathX0) * 0.5f, CathZ0 + (CathZ1 - CathZ0) * 0.5f, out int oi, out int oj, out _, out _, out _, out _);
                    originI = oi; originJ = oj; originFor = Seed;
                }
            }
            if (i == originI && j == originJ) return Region.Hamlets;
            float h = DesertNoise.Hash01(i, j, Seed + 73);
            for (int k = 0; k < RegCum.Length; k++) if (h <= RegCum[k]) return (Region)k;
            return Region.Dunes;
        }

        /// <summary>The region at a point.</summary>
        public static Region RegionAt(float x, float z)
        {
            Nearest2(x, z, out int i1, out int j1, out _, out _, out _, out _);
            return RegionOfCell(i1, j1);
        }

        /// <summary>The two regions that meet near a point and how much the first counts (0.5..1: half and half right on the border, pure 160 m inside).</summary>
        public static void RegionBlend(float x, float z, out Region a, out Region b, out float wa)
        {
            Nearest2(x, z, out int i1, out int j1, out float d1, out int i2, out int j2, out float d2);
            a = RegionOfCell(i1, j1); b = RegionOfCell(i2, j2);
            wa = 0.5f + 0.5f * DesertNoise.Smooth(0f, 160f, d2 - d1);
        }

        static readonly float[] RegDesat = { 0f, 0.12f, 0.35f, 0.55f, 0.08f, 0.8f };

        /// <summary>The multiplier of the sand colour at a point and how much of its colour is taken away (0..1), the regions fading into each other across their borders.</summary>
        public static void TintAt(float x, float z, out float r, out float g, out float b, out float desat)
        {
            RegionBlend(x, z, out var ra, out var rb, out float wa);
            var ta = RegTint[(int)ra]; var tb = RegTint[(int)rb];
            r = tb[0] + (ta[0] - tb[0]) * wa; g = tb[1] + (ta[1] - tb[1]) * wa; b = tb[2] + (ta[2] - tb[2]) * wa;
            desat = RegDesat[(int)rb] + (RegDesat[(int)ra] - RegDesat[(int)rb]) * wa;
        }

        /// <summary>Brine on the salt flats: a film of water over the crust, in lakes a couple of hundred metres across that cover about a third of the flats' interior. 0 everywhere else and in
        /// the strip where the flats meet another region (there the crust stays dry), up to 1 in the middle of a lake. A pure function of the seed, so the shader (through the sand's vertex
        /// colour, see <see cref="BrineAlpha"/>), the footsteps (a splash, no prints) and the lab all agree.</summary>
        public static float Brine(float x, float z)
        {
            float salt = RegionWeight(Region.Salt, x, z);
            if (salt < 0.9f) return 0f;
            float lakes = DesertNoise.Smooth(0.54f, 0.62f, DesertNoise.Fbm(x / 230f + 31f, z / 160f - 17f, Seed + 301, 3));
            return lakes <= 0f ? 0f : lakes * DesertNoise.Smooth(0.90f, 0.99f, salt) * DesertBiomes.At(x, z, Seed).Desert;
        }

        /// <summary>The alpha of the sand's vertex colour at a point: 'desat' (how much colour the region takes from the sand, 0..0.8) and, where there is brine, 0.8 + a fifth of how deep it is, so
        /// that 0.8 to 1 means water to the shader (it decodes it as (alpha - 0.8) * 5) and nothing else reads the top fifth: the salt flats are the only region that gets to 0.8.</summary>
        public static float BrineAlpha(float x, float z, float desat)
        {
            float b = Brine(x, z);
            return b > 0f ? Math.Max(desat, 0.8f) + 0.2f * b : desat;
        }

        /// <summary>How tall the dunes are here compared with the standard (1).</summary>
        public static float RegionAmp(float x, float z)
        {
            RegionBlend(x, z, out var ra, out var rb, out float wa);
            return RegAmp[(int)rb] + (RegAmp[(int)ra] - RegAmp[(int)rb]) * wa;
        }

        /// <summary>How much of a region there is at a point (0..1).</summary>
        public static float RegionWeight(Region r, float x, float z)
        {
            RegionBlend(x, z, out var ra, out var rb, out float wa);
            return (ra == r ? wa : 0f) + (rb == r ? 1f - wa : 0f);
        }

        /// <summary>Distance from a point to the cathedral's footprint (0 inside it).</summary>
        public static float DistToCathedral(float x, float z)
        {
            float dx = Math.Max(Math.Max(CathX0 - x, x - CathX1), 0f), dz = Math.Max(Math.Max(CathZ0 - z, z - CathZ1), 0f);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Where a target lies as seen by someone who looks along (fx, fz): the horizontal vector to the target is (dx, dz). 0 ahead, 1 ahead on the right, 2 right,
        /// 3 behind on the right, 4 behind, 5 behind on the left, 6 left, 7 ahead on the left (sectors of 45 degrees; x is to the right and z ahead when one looks along +z).
        /// -1 when the target is on the spot or the look is undefined. The HUD says it in words ("a destra") so that the way home is never lost.</summary>
        public static int Sector(float dx, float dz, float fx, float fz)
        {
            if (dx * dx + dz * dz < 1e-4f || fx * fx + fz * fz < 1e-6f) return -1;
            double a = Math.Atan2(fz * dx - fx * dz, fx * dx + fz * dz) * 180.0 / Math.PI;      // + = to the right
            int s = (int)Math.Round(a / 45.0);
            return ((s % 8) + 8) % 8;
        }

        /// <summary>The cathedral's centre: the landmark the HUD points to.</summary>
        public static void CathedralCentre(out float x, out float z) { x = (CathX0 + CathX1) * 0.5f; z = (CathZ0 + CathZ1) * 0.5f; }

        // ------------------------------------------------------------------ the ground
        // how much of the transverse dunes each region has: the Badlands have mesas instead, the salt flats are flat
        static readonly float[] RegDune = { 1.00f, 0.45f, 0.60f, 0.40f, 0.30f, 0.08f };

        /// <summary>Transverse dunes, in metres above the flat sand: crests that run across the wind and wander, a long gentle windward face and a steep slip face (about 30 degrees),
        /// grouped in fields with flat floors between. u runs along the wind, v across it; 'far' (0..1) is the growth with the distance from home: the dunes get longer and taller.</summary>
        static float Dunes(float u, float v, float far)
        {
            float wl = (120f + 110f * DesertNoise.Fbm(u / 800f + 5f, v / 800f - 3f, Seed + 83, 2)) * DesertNoise.Lerp(1f, 1.6f, far);       // crest spacing, slowly changing
            float warp = 70f * DesertNoise.Fbm(u / 300f, v / 210f + 13f, Seed + 89, 3) + 18f * DesertNoise.Value(u / 70f, v / 55f, Seed + 97);   // the crest lines wander
            float ph = (u + warp) / wl;
            float f = ph - (float)Math.Floor(ph);
            const float Crest = 0.72f;                                                                   // the foot is at 0, the crest at 0.72, the slip face falls in the rest
            float prof = f < Crest ? (float)Math.Pow(f / Crest, 1.5) : 1f - DesertNoise.Smooth(0f, 1f, (f - Crest) / (1f - Crest));
            float field = DesertNoise.Smooth(0.32f, 0.56f, DesertNoise.Fbm(u / 520f + 3f, v / 340f - 7f, Seed + 101, 2));      // where there are dunes at all
            float k = 0.065f + 0.055f * DesertNoise.Fbm(u / 380f - 9f, v / 380f + 4f, Seed + 107, 2);                         // height over spacing: the taller, the steeper
            return prof * field * k * wl;
        }

        // ------------------------------------------------------------------ the buttes
        /// <summary>Buttes: lone landmarks of layered rock (a flat top, walls cut by gullies, a skirt of rubble round the foot) that rise 28 to 95 m over the sand. A square of 900 m
        /// has at most one, put in its middle 40% so that nothing of it crosses into the next square; its skirt stays 250 m away from the cathedral and 120 m from the welcome chapel
        /// and the landmark tower. They give every region a skyline: from the cathedral's stairs one is a mark on the horizon to walk to, and the sand shader paints the walls in
        /// strata like it does the mesas.</summary>
        public const float ButteCell = 900f, ButteKeepOut = 250f, ButteMaxReach = 255f, ButteReachRadii = 1.95f;
        static readonly float[] ButteChance = { 0.26f, 0.06f, 0.22f, 0.18f, 0.80f, 0.30f };         // dunes, hamlets, holy land, dead town, badlands, salt flats

        static void ButtePoint(int i, int j, out float px, out float pz)
        {
            px = (i + 0.30f + 0.40f * DesertNoise.Hash01(i, j, Seed + 211)) * ButteCell;
            pz = (j + 0.30f + 0.40f * DesertNoise.Hash01(i, j, Seed + 212)) * ButteCell;
        }

        /// <summary>The butte of a square of 900 m, if it has one: its middle, half length (m), height (m), the angle of its long axis and how much narrower than long it is (0.55 to 1).</summary>
        public static bool ButteOfCell(int i, int j, out float px, out float pz, out float radius, out float height, out float angle, out float aspect)
        {
            ButtePoint(i, j, out px, out pz);
            radius = 60f + 70f * DesertNoise.Hash01(i, j, Seed + 213);
            height = 28f + 67f * (float)Math.Pow(DesertNoise.Hash01(i, j, Seed + 214), 1.3);
            angle = DesertNoise.Hash01(i, j, Seed + 215) * 3.14159f;
            aspect = 0.55f + 0.45f * DesertNoise.Hash01(i, j, Seed + 216);
            float reach = ButteReachRadii * radius;
            if (DistToCathedral(px, pz) - reach < ButteKeepOut) return false;
            float wx = px - WelcomeX, wz = pz - WelcomeZ, lx = px - LandmarkX, lz = pz - LandmarkZ;
            if (Math.Sqrt(wx * wx + wz * wz) - reach < 120f || Math.Sqrt(lx * lx + lz * lz) - reach < 120f) return false;
            return DesertNoise.Hash01(i, j, Seed + 217) < ButteChance[(int)RegionAt(px, pz)];
        }

        /// <summary>The height of the buttes at a point (m over the sand): 0 away from them, the rubble skirt first (a fifth of the height, a gentle slope that can be walked), then a wall
        /// of 70 degrees with gullies in it, then a top that rolls a little.</summary>
        public static float Buttes(float x, float z)
        {
            int i = (int)Math.Floor(x / ButteCell), j = (int)Math.Floor(z / ButteCell);
            ButtePoint(i, j, out float px, out float pz);
            float dx = x - px, dz = z - pz;
            if (dx * dx + dz * dz > ButteMaxReach * ButteMaxReach) return 0f;                      // the cheap test first: nearly every call ends here
            if (!ButteOfCell(i, j, out _, out _, out float radius, out float height, out float angle, out float aspect)) return 0f;
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            float u = (dx * ca + dz * sa) / radius, v = (-dx * sa + dz * ca) / (radius * aspect);
            // an uneven outline: big bulges and, on top of them, the gullies that cut the wall
            float outline = 1f + 0.30f * (DesertNoise.Fbm(x / 70f + 3f, z / 70f - 8f, Seed + 221, 2) - 0.5f) + 0.20f * (DesertNoise.Fbm(x / 13f - 5f, z / 13f + 2f, Seed + 223, 3) - 0.5f);
            float rn = (float)Math.Sqrt(u * u + v * v) / outline;
            float wall = 1f - DesertNoise.Smooth(0.80f, 1.00f, rn);                                 // 1 on the top, 0 outside the wall
            float skirt = 1f - DesertNoise.Smooth(0.90f, 1.55f, rn);                                // the rubble round the foot
            float t2 = DesertNoise.Smooth(45f, 70f, height);                                        // the tall ones have a second step: a smaller top on the first
            float tier = (1f - DesertNoise.Smooth(0.45f, 0.58f, rn)) * t2;
            float roll = (DesertNoise.Fbm(x / 40f + 7f, z / 40f + 1f, Seed + 225, 2) - 0.5f) * 0.06f * height;
            return 0.20f * height * skirt + (0.80f - 0.10f * t2) * height * wall + 0.10f * height * tier + roll * wall;
        }

        /// <summary>True if a butte (its wall or the rubble round it) touches the rectangle grown by 'margin': no place is built there, because the pad of a place cut into a wall would be a cliff with a floor.</summary>
        public static bool ButteTouches(float x0, float z0, float x1, float z1, float margin)
        {
            x0 -= margin; z0 -= margin; x1 += margin; z1 += margin;
            for (int a = 0; a <= 4; a++)
                for (int b = 0; b <= 4; b++)
                    if (Buttes(x0 + (x1 - x0) * a / 4f, z0 + (z1 - z0) * b / 4f) > 0.3f) return true;
            return false;
        }

        /// <summary>The sand without the flat pads of the sites: transverse dunes that grow longer and taller with the distance, mesas with cliffs, and the buttes.</summary>
        public static float Raw(float x, float z)
        {
            float d = DistToCathedral(x, z);
            float mask = DesertNoise.Smooth(55f, 200f, d);
            if (mask <= 0f) return GroundY;
            var biome = DesertBiomes.At(x, z, Seed);
            if (biome.Desert <= 0f) return DesertBiomes.Relief(x, z, Seed, biome, GroundY);
            float u = x * 0.87f + z * 0.5f, v = -x * 0.5f + z * 0.87f;
            float swell = DesertNoise.Fbm(u / 420f, v / 300f, Seed + 11, 4);
            float ripple = DesertNoise.Value(u / 5f, v / 2.6f, Seed + 31);
            float far = DesertNoise.Smooth(300f, 4000f, d);
            RegionBlend(x, z, out var ra, out var rb, out float wa);
            float regAmp = RegAmp[(int)rb] + (RegAmp[(int)ra] - RegAmp[(int)rb]) * wa;
            float duneW = RegDune[(int)rb] + (RegDune[(int)ra] - RegDune[(int)rb]) * wa;
            float badlands = (ra == Region.Badlands ? wa : 0f) + (rb == Region.Badlands ? 1f - wa : 0f);
            float amp = DesertNoise.Lerp(5.5f, 15f, far) * regAmp;
            float knoll = DesertNoise.Fbm(x / 46f + 9f, z / 46f - 5f, Seed + 61, 3) * 2.6f * Math.Min(1f, amp / 7f);        // the swells a person walks over
            float h = swell * amp * 0.8f + Dunes(u, v, far) * duneW * Math.Min(regAmp, 1.25f) + ripple * 0.22f + knoll;
            // mesas: flat tops with walls (a wall is a few metres wide), each with its own height, a second step on the big ones, rough faces
            float mesaN = DesertNoise.Fbm(x / 650f + 40f, z / 650f - 17f, Seed + 47, 3);
            float edge = 0.60f - 0.12f * badlands;
            float mesaOn = DesertNoise.Smooth(edge, edge + 0.028f, mesaN);
            float tier2 = DesertNoise.Smooth(edge + 0.09f, edge + 0.112f, mesaN);
            float mesaH = 7f + 15f * DesertNoise.Fbm(x / 1500f - 3f, z / 1500f + 8f, Seed + 53, 2);
            float erode = (DesertNoise.Fbm(x / 24f + 5f, z / 24f - 3f, Seed + 59, 3) - 0.5f) * 9f * (4f * mesaOn * (1f - mesaOn) + 0.15f * mesaOn);
            float mesa = (mesaOn * mesaH + tier2 * 5f + erode) * (0.65f + 0.35f * far) * (1f + 0.5f * badlands);
            float desert = GroundY + mask * (h + mesa + Buttes(x, z));
            return DesertBiomes.Relief(x, z, Seed, biome, desert);
        }

        /// <summary>The height of the sand at a point (m): the dunes, calmed and then flattened under the sites.</summary>
        [ThreadStatic] static Site[] nearBuf;
        [ThreadStatic] static float[] distBuf;

        public static float Height(float x, float z)
        {
            float dry = DryHeight(x, z);
            var current = lakes;
            for (int i = 0; i < current.Count; i++)
            {
                var lake = current[i];
                float radius = lake.RadiusAt(x, z);
                if (radius >= DesertLake.BankRadius) continue;
                float bed = lake.Surface - DesertLake.BedDepth(radius);
                return DesertNoise.Lerp(bed, dry, DesertNoise.Smooth(1.10f, DesertLake.BankRadius, radius));
            }
            return dry;
        }

        static float DryHeight(float x, float z)
        {
            float h = Raw(x, z);
            int cx = (int)Math.Floor(x / Cell), cz = (int)Math.Floor(z / Cell);
            var near = nearBuf ??= new Site[9]; var dists = distBuf ??= new float[9];
            int n = 0;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    var s = SiteOf(cx + i, cz + j);
                    if (s == null) continue;
                    float dx = Math.Max(Math.Max(s.fx0 - PadMargin - x, x - (s.fx1 + PadMargin)), 0f);
                    float dz = Math.Max(Math.Max(s.fz0 - PadMargin - z, z - (s.fz1 + PadMargin)), 0f);
                    near[n] = s; dists[n] = (float)Math.Sqrt(dx * dx + dz * dz); n++;
                }
            // first every site calms the dunes around it, then the pads go on top: two sites close together never tilt each other's floor
            for (int k = 0; k < n; k++)
            {
                float calm = CalmStrength * (1f - DesertNoise.Smooth(0f, CalmRadius, dists[k]));
                if (calm > 0f) h = DesertNoise.Lerp(h, near[k].padY, calm);
            }
            for (int k = 0; k < n; k++)
            {
                float pad = 1f - DesertNoise.Smooth(0f, PadBlend, dists[k]);
                if (pad > 0f) h = DesertNoise.Lerp(h, near[k].padY - 0.08f, pad);
            }
            // the apron of the cathedral: dead flat for 30 m (the stairs end there), then the dunes take over
            float apron = 1f - DesertNoise.Smooth(30f, 80f, DistToCathedral(x, z));
            return apron > 0f ? DesertNoise.Lerp(h, GroundY, apron) : h;
        }

        // ------------------------------------------------------------------ the sites
        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        /// <summary>The site of a 200 m cell (null if the cell is empty). Cached: the plans are made the first time somebody asks.</summary>
        public static Site SiteOf(int cx, int cz)
        {
            long key = Key(cx, cz);
            lock (gate)
            {
                if (cache.TryGetValue(key, out var s)) return s;
                s = MakeSite(cx, cz);
                cache[key] = s;
                return s;
            }
        }

        /// <summary>True if the site of this cell has been made already (a worker thread never has to make one).</summary>
        public static bool Known(int cx, int cz)
        {
            lock (gate) return cache.ContainsKey(Key(cx, cz));
        }

        /// <summary>Every cell a ground height or a prop of the square x0..x1, z0..z1 can look at (two cells around it) has its site made: a worker thread can work on the square.</summary>
        public static bool KnownAround(float x0, float z0, float x1, float z1)
        {
            int a = CellOf(x0) - 2, b = CellOf(x1) + 2, c = CellOf(z0) - 2, d = CellOf(z1) + 2;
            lock (gate)
            {
                for (int i = a; i <= b; i++)
                    for (int j = c; j <= d; j++)
                        if (!cache.ContainsKey(Key(i, j))) return false;
            }
            return true;
        }

        /// <summary>Every site whose footprint can come within 'radius' metres of a point.</summary>
        public static void SitesNear(float x, float z, float radius, List<Site> into)
        {
            into.Clear();
            int c0x = (int)Math.Floor((x - radius) / Cell), c1x = (int)Math.Floor((x + radius) / Cell);
            int c0z = (int)Math.Floor((z - radius) / Cell), c1z = (int)Math.Floor((z + radius) / Cell);
            for (int i = c0x; i <= c1x; i++)
                for (int j = c0z; j <= c1z; j++)
                {
                    var s = SiteOf(i, j);
                    if (s == null) continue;
                    float dx = Math.Max(Math.Max(s.fx0 - x, x - s.fx1), 0f), dz = Math.Max(Math.Max(s.fz0 - z, z - s.fz1), 0f);
                    if (dx * dx + dz * dz <= radius * radius) into.Add(s);
                }
        }

        /// <summary>Come <see cref="SitesNear"/>, ma solo i luoghi gia' fatti: nessuna cella nuova (fare un luogo costa fino a qualche millisecondo, e lo fa
        /// il terreno a poco a poco). Per chi gira ogni fotogramma sul thread principale.</summary>
        public static void SitesNearKnown(float x, float z, float radius, List<Site> into)
        {
            into.Clear();
            int c0x = (int)Math.Floor((x - radius) / Cell), c1x = (int)Math.Floor((x + radius) / Cell);
            int c0z = (int)Math.Floor((z - radius) / Cell), c1z = (int)Math.Floor((z + radius) / Cell);
            for (int i = c0x; i <= c1x; i++)
                for (int j = c0z; j <= c1z; j++)
                {
                    Site s;
                    lock (gate) { if (!cache.TryGetValue(Key(i, j), out s)) continue; }
                    if (s == null) continue;
                    float dx = Math.Max(Math.Max(s.fx0 - x, x - s.fx1), 0f), dz = Math.Max(Math.Max(s.fz0 - z, z - s.fz1), 0f);
                    if (dx * dx + dz * dz <= radius * radius) into.Add(s);
                }
        }

        static BuildingKind BuildingOf(SiteKind k)
        {
            switch (k)
            {
                case SiteKind.Casa: return BuildingKind.Casa;
                case SiteKind.Cappella: return BuildingKind.Cappella;
                case SiteKind.Locanda: return BuildingKind.Locanda;
                case SiteKind.Chiesa: return BuildingKind.Chiesa;
                case SiteKind.Albergo: return BuildingKind.Albergo;
                case SiteKind.Teatro: return BuildingKind.Teatro;
                case SiteKind.Sanatorio: return BuildingKind.Sanatorio;
                default: return BuildingKind.Complesso;
            }
        }

        static SiteKind PickKind(Rng r, Region reg)
        {
            var kinds = new[] { SiteKind.Casa, SiteKind.Cappella, SiteKind.Locanda, SiteKind.Chiesa, SiteKind.Albergo, SiteKind.Teatro, SiteKind.Sanatorio, SiteKind.Complesso, SiteKind.Torre, SiteKind.Catacomba };
            return kinds[r.Weighted(RegKinds[(int)reg])];
        }

        /// <summary>Puts a footprint (half sizes in m) at its own spot of the free part of the cell, CellMargin from the edges (the forced sites keep the place the design gave them).</summary>
        static void PlaceInCell(Site s, float halfW, float halfD)
        {
            if (s.forced) return;
            float x0 = s.cx * Cell + CellMargin + halfW, x1 = (s.cx + 1) * Cell - CellMargin - halfW;
            float z0 = s.cz * Cell + CellMargin + halfD, z1 = (s.cz + 1) * Cell - CellMargin - halfD;
            s.x = (float)Math.Round(x1 > x0 ? x0 + (x1 - x0) * s.jx : (x0 + x1) * 0.5f, 1);
            s.z = (float)Math.Round(z1 > z0 ? z0 + (z1 - z0) * s.jz : (z0 + z1) * 0.5f, 1);
        }

        static Site MakeSite(int cx, int cz)
        {
            float mx = (cx + 0.5f) * Cell, mz = (cz + 0.5f) * Cell;
            bool welcome = cx == CellOf(WelcomeX) && cz == CellOf(WelcomeZ), landmark = cx == CellOf(LandmarkX) && cz == CellOf(LandmarkZ);      // a chapel near the stairs and a tower on the horizon, always
            bool forced = welcome || landmark;
            if (!forced && DistToCathedral(mx, mz) < 170f) return null;        // with the half of a cell, the nearest edge of a site stays beyond 100 m
            var reg = RegionAt(mx, mz);
            if (!forced && DesertNoise.Hash01(cx, cz, Seed + 1) > RegDensity[(int)reg]) return null;
            int seed = (int)(DesertNoise.Hash(cx, cz, Seed + 2) & 0x7fffffff);
            var r = new Rng(seed);
            var kind = welcome ? SiteKind.Cappella : landmark ? SiteKind.Torre : PickKind(r, reg);
            var state = (BuildingState)r.Weighted(RegStates[(int)reg]);
            if (welcome) state = BuildingState.Ruin;
            if (kind == SiteKind.Casa && state == BuildingState.Split) state = BuildingState.Ruin;
            if (kind == SiteKind.Torre || kind == SiteKind.Catacomba) state = state == BuildingState.Intact || state == BuildingState.Split ? BuildingState.Intact : BuildingState.Ruin;
            var s = new Site { cx = cx, cz = cz, seed = seed, kind = kind, state = state, region = reg, forced = forced, palette = (seed >> 3) & 63 };
            s.jx = DesertNoise.Hash01(cx, cz, Seed + 3); s.jz = DesertNoise.Hash01(cx, cz, Seed + 4);
            s.x = mx; s.z = mz;
            if (welcome) { s.x = WelcomeX; s.z = WelcomeZ; }
            if (landmark) { s.x = LandmarkX; s.z = LandmarkZ; }
            bool ok = s.IsStackKind ? MakeStack(s, r) : MakeBuilding(s, r);
            if (!ok)
            {
                // the generator gave up on this seed: a hut, which always comes out
                s.kind = SiteKind.Casa; s.levels = null; s.state = BuildingState.Ruin;
                if (!MakeBuilding(s, r)) return null;
            }
            if (!forced && ButteTouches(s.fx0, s.fz0, s.fx1, s.fz1, 45f)) return null;       // a butte there: the pad and the calm round the place would cut it
            if (!forced && s.IsTower && DesertBiomes.At(s.x, s.z, Seed).Ice > 0.83f && DesertBiomes.IceSpur(s.x, s.z, Seed) > 0.12f)
                return null;       // keep the high ridgeline clear; towers remain among the foothills
            s.padY = Math.Max(GroundY, Raw(s.x, s.z));
            return s;
        }

        static bool MakeBuilding(Site s, Rng r)
        {
            Plan raw = null;
            for (int k = 0; k < 6 && raw == null; k++) raw = PlanGen.Building(s.seed + k * 977, BuildingOf(s.kind), s.state);
            if (raw == null) return false;
            int rot = r.Range(0, 4);
            bool mirror = r.Chance(0.5);
            var turned = PlanGen.Place(raw, rot, mirror, 0, 0);
            PlaceInCell(s, (turned.maxX - turned.minX) * 0.05f, (turned.maxZ - turned.minZ) * 0.05f);
            int dx = (int)Math.Round(s.x * 10f) - (turned.minX + turned.maxX) / 2, dz = (int)Math.Round(s.z * 10f) - (turned.minZ + turned.maxZ) / 2;
            s.plan = PlanGen.Place(turned, 0, false, dx, dz);
            s.theme = s.plan.theme;
            s.fx0 = s.plan.minX * 0.1f; s.fx1 = s.plan.maxX * 0.1f; s.fz0 = s.plan.minZ * 0.1f; s.fz1 = s.plan.maxZ * 0.1f;
            return true;
        }

        /// <summary>A tower (7 to 14 floors, the boss on the top one) or the mausoleum over a catacomb (3 to 5 levels, the boss on the deepest).</summary>
        static bool MakeStack(Site s, Rng r)
        {
            bool tower = s.kind == SiteKind.Torre;
            int n = tower ? r.Range(7, 15) : r.Range(3, 6);
            s.church = !tower || r.Chance(0.25);
            s.theme = tower && !s.church ? (r.Chance(0.5) ? Theme.Albergo : Theme.Sanatorio) : Theme.Albergo;
            s.entrance = tower ? 0 : n - 1;
            s.boss = tower ? n - 1 : 0;
            s.levels = new Plan[n];
            bool broken = s.state == BuildingState.Ruin;
            for (int i = 0; i < n; i++)
            {
                var local = PlanGen.StackLevel(s.seed, i, n, s.entrance, s.boss, s.theme, s.church, broken);
                if (i == 0)
                {
                    local.ComputeBounds();
                    PlaceInCell(s, (local.maxX - local.minX) * 0.05f, (local.maxZ - local.minZ) * 0.05f);
                    s.stackDx = (int)Math.Round(s.x * 10f) - (local.minX + local.maxX) / 2;
                    s.stackDz = (int)Math.Round(s.z * 10f) - (local.minZ + local.maxZ) / 2;
                }
                s.levels[i] = PlanGen.Place(local, 0, false, s.stackDx, s.stackDz);
            }
            var b = s.levels[0];
            s.fx0 = b.minX * 0.1f; s.fx1 = b.maxX * 0.1f; s.fz0 = b.minZ * 0.1f; s.fz1 = b.maxZ * 0.1f;
            return true;
        }
    }
}
