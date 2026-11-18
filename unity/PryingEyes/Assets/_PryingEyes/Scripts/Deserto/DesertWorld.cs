using System;
using System.Collections.Generic;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    public enum SiteKind { Casa, Cappella, Locanda, Chiesa, Albergo, Teatro, Sanatorio, Complesso, Torre, Catacomba }

    /// <summary>A place of the desert: a building (whole, ruined, split or collapsing), a tower of many floors or the mausoleum that leads down to the
    /// catacombs. Everything about it is decided by the world seed and its cell, so it is the same every time the player comes back.</summary>
    public sealed class Site
    {
        public int cx, cz, seed;
        public SiteKind kind;
        public BuildingState state;
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

    /// <summary>The desert as a pure function of one seed: the ground, the sites on it, where the sand is flat. Nothing is stored: the terrain chunks, the
    /// buildings and the navmesh window all ask this class. tools/desertlab checks it. The cathedral stands at the origin on a stone podium; the sand
    /// around it is flat for the first 70 m and the dunes grow with the distance.</summary>
    public static class DesertWorld
    {
        public static int Seed = 1;

        /// <summary>The sand level next to the cathedral (m): the stairs of the churchyard go down to it from the cathedral's floor (y = 0).</summary>
        public const float GroundY = -2.4f;
        /// <summary>The cathedral's footprint (m): the rooms of HubPlan, with the armoury that the desert adds on the east side (up to x = 47).</summary>
        public const float CathX0 = 0f, CathZ0 = 0f, CathX1 = 47f, CathZ1 = 66f;
        public const float Cell = 200f;
        public const float SiteDensity = 0.60f;
        /// <summary>The flat apron around a footprint, and how far the sand is calmed down beyond it.</summary>
        public const float PadMargin = 4f, PadBlend = 6f, CalmRadius = 90f;
        /// <summary>Where the desert ends: beyond it floats lose precision, so the director turns the walker back (<c>DesertDirector.EdgeTick</c>).</summary>
        public const float WorldRadius = 18000f;

        static readonly Dictionary<long, Site> cache = new Dictionary<long, Site>();
        /// <summary>The terrain is computed on worker threads and asks for the sites around each point: the cache is shared, so every access is locked.
        /// The director makes the sites of a chunk's cells on the main thread before the chunk is queued, so a worker normally only finds them ready.</summary>
        static readonly object gate = new object();

        public static void Reset(int seed)
        {
            lock (gate)
            {
                Seed = seed;
                cache.Clear();
            }
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
        /// <summary>The sand without the flat pads of the sites: dunes whose crests grow taller with the distance, and mesas.</summary>
        public static float Raw(float x, float z)
        {
            float d = DistToCathedral(x, z);
            float mask = DesertNoise.Smooth(70f, 300f, d);
            if (mask <= 0f) return GroundY;
            float u = x * 0.87f + z * 0.5f, v = -x * 0.5f + z * 0.87f;
            float swell = DesertNoise.Fbm(u / 420f, v / 300f, Seed + 11, 4);
            float crest = DesertNoise.Ridged(u / 120f, v / 70f, Seed + 23, 3);
            float ripple = DesertNoise.Value(u / 5f, v / 2.6f, Seed + 31);
            float far = DesertNoise.Smooth(300f, 4000f, d);
            float amp = DesertNoise.Lerp(4.5f, 15f, far);
            float h = swell * amp * 1.2f + crest * amp * 0.85f + ripple * 0.22f;
            float mesaN = DesertNoise.Fbm(x / 650f + 40f, z / 650f - 17f, Seed + 47, 3);
            float mesa = DesertNoise.Smooth(0.60f, 0.70f, mesaN) * (5f + 11f * DesertNoise.Fbm(x / 170f, z / 170f, Seed + 53, 2)) * (0.4f + 0.6f * far);
            return GroundY + mask * (h + mesa);
        }

        /// <summary>The height of the sand at a point (m): the dunes, calmed and then flattened under the sites.</summary>
        public static float Height(float x, float z)
        {
            float h = Raw(x, z);
            int cx = (int)Math.Floor(x / Cell), cz = (int)Math.Floor(z / Cell);
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    var s = SiteOf(cx + i, cz + j);
                    if (s == null) continue;
                    float dx = Math.Max(Math.Max(s.fx0 - PadMargin - x, x - (s.fx1 + PadMargin)), 0f);
                    float dz = Math.Max(Math.Max(s.fz0 - PadMargin - z, z - (s.fz1 + PadMargin)), 0f);
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    float calm = 0.78f * (1f - DesertNoise.Smooth(0f, CalmRadius, dist));
                    if (calm > 0f) h = DesertNoise.Lerp(h, s.padY, calm);
                    float pad = 1f - DesertNoise.Smooth(0f, PadBlend, dist);
                    if (pad > 0f) h = DesertNoise.Lerp(h, s.padY - 0.08f, pad);
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

        static SiteKind PickKind(Rng r)
        {
            var kinds = new[] { SiteKind.Casa, SiteKind.Cappella, SiteKind.Locanda, SiteKind.Chiesa, SiteKind.Albergo, SiteKind.Teatro, SiteKind.Sanatorio, SiteKind.Complesso, SiteKind.Torre, SiteKind.Catacomba };
            return kinds[r.Weighted(new double[] { 20, 12, 12, 7, 6, 4, 4, 4, 16, 15 })];
        }

        static Site MakeSite(int cx, int cz)
        {
            float mx = (cx + 0.5f) * Cell, mz = (cz + 0.5f) * Cell;
            bool welcome = cx == 0 && cz == -1, landmark = cx == 0 && cz == -2;      // a chapel near the stairs and a tower on the horizon, always
            bool forced = welcome || landmark;
            if (!forced && DistToCathedral(mx, mz) < 150f) return null;
            if (!forced && DesertNoise.Hash01(cx, cz, Seed + 1) > SiteDensity) return null;
            int seed = (int)(DesertNoise.Hash(cx, cz, Seed + 2) & 0x7fffffff);
            var r = new Rng(seed);
            var kind = welcome ? SiteKind.Cappella : landmark ? SiteKind.Torre : PickKind(r);
            var state = (BuildingState)r.Weighted(new double[] { 30, 26, 22, 22 });
            if (welcome) state = BuildingState.Ruin;
            if (kind == SiteKind.Casa && state == BuildingState.Split) state = BuildingState.Ruin;
            if (kind == SiteKind.Torre || kind == SiteKind.Catacomba) state = state == BuildingState.Intact || state == BuildingState.Split ? BuildingState.Intact : BuildingState.Ruin;
            var s = new Site { cx = cx, cz = cz, seed = seed, kind = kind, state = state };
            s.x = (float)Math.Round(mx + (DesertNoise.Hash01(cx, cz, Seed + 3) - 0.5f) * 50f, 1);
            s.z = (float)Math.Round(mz + (DesertNoise.Hash01(cx, cz, Seed + 4) - 0.5f) * 50f, 1);
            if (welcome) { s.x = 100f; s.z = -100f; }
            if (landmark) { s.x = 100f; s.z = -300f; }
            s.padY = Math.Max(GroundY, Raw(s.x, s.z));
            bool ok = s.IsStackKind ? MakeStack(s, r) : MakeBuilding(s, r);
            if (!ok)
            {
                // the generator gave up on this seed: a hut, which always comes out
                s.kind = SiteKind.Casa; s.levels = null; s.state = BuildingState.Ruin;
                if (!MakeBuilding(s, r)) return null;
            }
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
