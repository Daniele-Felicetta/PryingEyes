using System;
using System.Collections.Generic;
using System.Diagnostics;
using PryingEyes.Deserto;
using PryingEyes.Proc;

/// <summary>Checks of the desert's pure code (ground, sites, clock) and of the building and stack generators, with no Unity: tools/desertlab.
/// Usage: dotnet run --project tools/desertlab [-- --quick]</summary>
static class Program
{
    static bool ok = true;
    static void Check(string code, bool passed, string observed)
    {
        ok &= passed;
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {code}: {observed}");
    }

    static int Main(string[] args)
    {
        bool quick = Array.IndexOf(args, "--quick") >= 0;
        Noise();
        Clock();
        Ground();
        Terrain();
        Buildings(quick ? 120 : 600);
        Stacks(quick ? 40 : 200);
        World(quick ? 6 : 14);
        Compass();
        Console.WriteLine(ok ? "ALL PASS" : "FAILED");
        return ok ? 0 : 1;
    }

    // ------------------------------------------------------------------ noise
    static void Noise()
    {
        bool same = true, range = true;
        for (int i = 0; i < 2000; i++)
        {
            float x = i * 3.17f - 1000f, z = i * 1.91f + 55f;
            float a = DesertNoise.Fbm(x, z, 7), b = DesertNoise.Fbm(x, z, 7);
            if (a != b) same = false;
            if (a < 0f || a >= 1f || DesertNoise.Ridged(x, z, 3) < 0f || DesertNoise.Ridged(x, z, 3) > 1f || DesertNoise.Value(x, z, 1) < 0f || DesertNoise.Value(x, z, 1) >= 1f) range = false;
        }
        Check("NOISE_DETERMINISTIC", same, "same seed, same value");
        Check("NOISE_RANGE", range, "fbm, ridged and value stay inside their ranges");
        int below = 0;
        for (int i = 0; i < 4000; i++) if (DesertNoise.Hash01(i, -i * 7, 5) < 0.5f) below++;
        Check("NOISE_HASH_BALANCED", below > 1850 && below < 2150, below + " of 4000 under one half");
    }

    // ------------------------------------------------------------------ clock
    static void Clock()
    {
        var start = DesertClock.Evaluate(DesertClock.StartHour);
        var noon = DesertClock.Evaluate(12f);
        var midnight = DesertClock.Evaluate(0f);
        Check("CLOCK_STARTS_IN_DAYLIGHT", start.daylight > 0.55f && start.sunHeight > 0.1f, $"07:00 daylight {start.daylight:F2}, sun height {start.sunHeight:F2}");
        Check("CLOCK_NOON_HIGHEST", noon.sunHeight > start.sunHeight && noon.sunHeight > 0.9f, $"noon sun height {noon.sunHeight:F2}");
        Check("CLOCK_MIDNIGHT_DARK", midnight.daylight < 0.01f && midnight.night > 0.95f && midnight.stars > 0.9f && midnight.moonY > 0.5f, $"midnight daylight {midnight.daylight:F2} stars {midnight.stars:F2}");
        Check("CLOCK_SUN_AND_MOON_OPPOSITE", Math.Abs(noon.sunY + noon.moonY) < 1e-5f && Math.Abs(noon.sunX + noon.moonX) < 1e-5f, "the moon is always opposite the sun");
        bool unit = true, monotone = true;
        float prev = -2f;
        for (float h = 0f; h < 24f; h += 0.25f)
        {
            var s = DesertClock.Evaluate(h);
            float len = (float)Math.Sqrt(s.sunX * s.sunX + s.sunY * s.sunY + s.sunZ * s.sunZ);
            if (Math.Abs(len - 1f) > 1e-3f) unit = false;
            if (h >= 6f && h <= 12f) { if (s.sunHeight < prev - 1e-4f) monotone = false; prev = s.sunHeight; }
        }
        Check("CLOCK_SUN_DIRECTION_UNIT", unit, "the sun direction is a unit vector all day");
        Check("CLOCK_SUN_RISES_UNTIL_NOON", monotone, "sun height only grows from 06:00 to 12:00");
        var dawn = DesertClock.Evaluate(6.2f);
        Check("CLOCK_DAWN_IS_ORANGE", dawn.sunColor.r > dawn.sunColor.b * 1.6f, $"dawn sun colour {dawn.sunColor.r:F2}/{dawn.sunColor.g:F2}/{dawn.sunColor.b:F2}");
        var clock = new DesertClock();
        for (int i = 0; i < 1200; i++) clock.Advance(1f);
        Check("CLOCK_FULL_DAY_LENGTH", clock.day == 1 && Math.Abs(clock.hour - DesertClock.StartHour) < 0.05f, $"after {DesertClock.DaySeconds}s: day {clock.day}, hour {clock.hour:F2}");
    }

    // ------------------------------------------------------------------ ground
    static void Ground()
    {
        DesertWorld.Reset(12345);
        float maxFlat = 0f;
        for (float x = -50f; x <= 90f; x += 7f)
            for (float z = -60f; z <= 12f; z += 6f)
                if (DesertWorld.DistToCathedral(x, z) < 30f) maxFlat = Math.Max(maxFlat, Math.Abs(DesertWorld.Height(x, z) - DesertWorld.GroundY));
        Check("GROUND_FLAT_AROUND_CATHEDRAL", maxFlat < 0.001f, $"largest deviation {maxFlat:F4} m within 30 m of the cathedral");
        // the same seed gives the same ground; another seed does not
        float a = DesertWorld.Height(1234.5f, -987.6f);
        DesertWorld.Reset(12345);
        float b = DesertWorld.Height(1234.5f, -987.6f);
        DesertWorld.Reset(999);
        float c = DesertWorld.Height(1234.5f, -987.6f);
        Check("GROUND_DETERMINISTIC", a == b && a != c, $"{a:F2} {b:F2} / other seed {c:F2}");
        DesertWorld.Reset(12345);
        // slopes on a 2 m grid, away from the sites' own pads: the sand must be walkable almost everywhere
        float worst = 0f, tall = float.MinValue, low = float.MaxValue;
        int steep = 0, n = 0;
        for (float x = -1500f; x <= 1500f; x += 31.3f)
            for (float z = -1500f; z <= 1500f; z += 29.7f)
            {
                float h0 = DesertWorld.Height(x, z), hx = DesertWorld.Height(x + 2f, z), hz = DesertWorld.Height(x, z + 2f);
                float slope = Math.Max(Math.Abs(hx - h0), Math.Abs(hz - h0)) / 2f;
                worst = Math.Max(worst, slope); tall = Math.Max(tall, h0); low = Math.Min(low, h0);
                if (slope > 0.9f) steep++;
                n++;
            }
        Check("GROUND_NOT_BELOW_SEA", low >= DesertWorld.GroundY - 0.2f, $"lowest {low:F2} m (sea level {DesertWorld.GroundY} m)");
        Check("GROUND_MOSTLY_WALKABLE", steep < n * 0.02f, $"{steep} of {n} samples steeper than 42 degrees (worst {Math.Atan(worst) * 57.3f:F0} deg), highest {tall:F1} m");
        // far away the dunes are taller than near the cathedral
        float nearMax = 0f, farMax = 0f;
        for (float x = 100f; x < 700f; x += 9f) nearMax = Math.Max(nearMax, DesertWorld.Raw(x, 150f) - DesertWorld.GroundY);
        for (float x = 8000f; x < 8600f; x += 9f) farMax = Math.Max(farMax, DesertWorld.Raw(x, 150f) - DesertWorld.GroundY);
        Check("GROUND_DUNES_GROW_WITH_DISTANCE", farMax > nearMax * 1.2f, $"{nearMax:F1} m near, {farMax:F1} m far");
    }

    // ------------------------------------------------------------------ the sand mesh
    /// <summary>Area of the chunk seen from above (m2), and whether every triangle faces the way it should: the sand up, the skirts outwards.</summary>
    static double Projected(ChunkData d, out bool facing, out bool unitNormals)
    {
        double area = 0; facing = true; unitNormals = true;
        float mid = DesertTerrainGen.ChunkSize * 0.5f;
        for (int t = 0; t < d.tris.Length; t += 3)
        {
            int i0 = d.tris[t] * 3, i1 = d.tris[t + 1] * 3, i2 = d.tris[t + 2] * 3;
            double ux = d.pos[i1] - d.pos[i0], uy = d.pos[i1 + 1] - d.pos[i0 + 1], uz = d.pos[i1 + 2] - d.pos[i0 + 2];
            double vx = d.pos[i2] - d.pos[i0], vy = d.pos[i2 + 1] - d.pos[i0 + 1], vz = d.pos[i2 + 2] - d.pos[i0 + 2];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            area += Math.Abs(ny) * 0.5;      // the shadow of the triangle on the ground (ny is twice its area when seen from above)
            if (Math.Abs(ny) > 1e-9)
            {
                if (ny < 0) facing = false;
            }
            else if (Math.Abs(nx) + Math.Abs(nz) > 1e-9)
            {
                double cx = (d.pos[i0] + d.pos[i1] + d.pos[i2]) / 3.0 - mid, cz = (d.pos[i0 + 2] + d.pos[i1 + 2] + d.pos[i2 + 2]) / 3.0 - mid;
                if (nx * cx + nz * cz <= 0) facing = false;
            }
        }
        for (int v = 0; v < d.vertexCount; v++)
        {
            double len = Math.Sqrt(d.nrm[v * 3] * d.nrm[v * 3] + d.nrm[v * 3 + 1] * d.nrm[v * 3 + 1] + d.nrm[v * 3 + 2] * d.nrm[v * 3 + 2]);
            if (Math.Abs(len - 1.0) > 1e-3) unitNormals = false;
        }
        return area;
    }

    static void Terrain()
    {
        DesertWorld.Reset(20261118);
        var cuts = new List<float> { 13.37f, 20.01f, 20.2f, 63.9f, -5f, 70f };
        var ax = DesertTerrainGen.Axis(0f, 64f, 2f, cuts);
        float minGap = float.MaxValue;
        for (int i = 1; i < ax.Length; i++) minGap = Math.Min(minGap, ax[i] - ax[i - 1]);
        bool hits = ax[0] == 0f && ax[ax.Length - 1] == 64f;
        foreach (float c in new[] { 13.37f, 20.01f, 20.2f, 63.9f }) hits &= Array.IndexOf(ax, c) >= 0;
        Check("TERRAIN_AXIS_KEEPS_THE_EDGES_AND_THE_CUTS", hits && minGap >= 0.05f, $"{ax.Length} lines, narrowest gap {minGap:F2} m, both ends and the four cuts are lines");

        // a plain chunk covers its whole square
        var plain = DesertTerrainGen.Build(3, -2, 4f, null);
        double flat = Projected(plain, out bool plainFacing, out bool plainUnit);
        Check("TERRAIN_CHUNK_COVERS_THE_SQUARE", Math.Abs(flat - 4096.0) < 0.01, $"{flat:F3} m2 of 4096");
        Check("TERRAIN_FACES_AND_NORMALS", plainFacing && plainUnit, "the sand faces up, the skirts face out, every normal is a unit vector");

        // a catacomb's well is cut out exactly: the missing area is the hole's, whatever the step and wherever the chunk border runs
        int wells = 0, chunks = 0, bad = 0; double worst = 0; string first = "";
        for (int cx = -14; cx <= 14; cx++)
            for (int cz = -14; cz <= 14; cz++)
            {
                var s = DesertWorld.SiteOf(cx, cz);
                if (s == null || !s.WellRect(out var r)) continue;
                wells++;
                int px = (int)Math.Floor((r.x0 + r.x1) * 0.5f / DesertTerrainGen.ChunkSize), pz = (int)Math.Floor((r.z0 + r.z1) * 0.5f / DesertTerrainGen.ChunkSize);
                foreach (float step in new[] { 1.5f, 4f, 8f })
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            float ox = (px + dx) * DesertTerrainGen.ChunkSize, oz = (pz + dz) * DesertTerrainGen.ChunkSize;
                            var d = DesertTerrainGen.Build(px + dx, pz + dz, step, new[] { r });
                            float lx = Math.Min(r.x1, ox + 64f) - Math.Max(r.x0, ox), lz = Math.Min(r.z1, oz + 64f) - Math.Max(r.z0, oz);
                            // a hole edge less than 1 cm inside a chunk edge gets no grid line of its own (see Axis): that hair of sand is not cut
                            float overlap = lx < 0.0105f || lz < 0.0105f ? 0f : lx * lz;
                            double got = Projected(d, out bool facing, out bool unit);
                            double err = Math.Abs(got - (4096.0 - overlap));
                            worst = Math.Max(worst, err);
                            chunks++;
                            if (err > 0.02 || !facing || !unit) { bad++; if (first.Length < 400) first += $"\n     {s} step {step} chunk ({px + dx},{pz + dz}) off by {err:F3} m2, facing {facing}, unit {unit}, hole x {r.x0:F3}..{r.x1:F3} z {r.z0:F3}..{r.z1:F3}"; }
                        }
            }
        Check("TERRAIN_WELL_CUT_EXACTLY", wells > 0 && bad == 0, wells == 0 ? "no catacomb found" : bad == 0 ? $"{wells} wells, {chunks} chunks at three steps, worst error {worst:F4} m2" : bad + " bad chunks, first: " + first);
    }

    // ------------------------------------------------------------------ the building generator
    static void Buildings(int count)
    {
        var rng = new Random(11);
        var kinds = (BuildingKind[])Enum.GetValues(typeof(BuildingKind));
        var states = (BuildingState[])Enum.GetValues(typeof(BuildingState));
        int built = 0, faults = 0, noDoor = 0, tooBig = 0, none = 0;
        var perKind = new Dictionary<BuildingKind, int>();
        var rooms = new Dictionary<BuildingKind, int>();
        var firstFault = "";
        var giveUps = new Dictionary<string, int>();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            var kind = kinds[i % kinds.Length];
            var state = states[(i / kinds.Length) % states.Length];
            var p = PlanGen.Building(rng.Next(1, 1 << 28), kind, state);
            if (p == null)
            {
                none++;
                string key = kind + "/" + state + ": " + PlanGen.LastBuildingFailure;
                giveUps[key] = (giveUps.TryGetValue(key, out int g) ? g : 0) + 1;
                continue;
            }
            built++;
            perKind[kind] = (perKind.TryGetValue(kind, out int c) ? c : 0) + 1;
            rooms[kind] = (rooms.TryGetValue(kind, out int r0) ? r0 : 0) + p.rooms.Count;
            string why = PlanGen.BuildingFault(p);
            if (why != null) { faults++; if (firstFault.Length == 0) firstFault = kind + "/" + state + ": " + why; }
            bool door = false;
            foreach (var o in p.openings) if (o.b < 0 && o.sill < 0.01f && !o.oneSided) door = true;
            if (!door) noDoor++;
            if (p.maxX - p.minX > PlanGen.BuildingSpan || p.maxZ - p.minZ > PlanGen.BuildingSpan) tooBig++;
            // every rotation and mirror keeps the plan valid
            var turned = PlanGen.Place(p, i % 4, i % 2 == 0, 40, -60);
            if (PlanGen.BuildingFault(turned) != null) faults++;
        }
        sw.Stop();
        Check("BUILDING_GENERATOR_FINDS_PLANS", none <= count / 25, $"{built} plans out of {count} ({none} gave up) in {sw.ElapsedMilliseconds} ms");
        Check("BUILDING_PLANS_VALID", faults == 0, faults == 0 ? "every plan passes BuildingFault, also turned and mirrored" : faults + " faults, first: " + firstFault);
        Check("BUILDING_HAS_A_DOOR_TO_THE_SAND", noDoor == 0, noDoor + " buildings without an entrance");
        Check("BUILDING_FITS_THE_CELL", tooBig == 0, tooBig + " buildings wider than " + PlanGen.BuildingSpan + " dm");
        foreach (var kv in giveUps) Console.WriteLine($"     gave up {kv.Value,3}x  {kv.Key}");
        foreach (var k in kinds)
            if (perKind.TryGetValue(k, out int n)) Console.WriteLine($"     {k,-10} {n,4} plans, {rooms[k] / (float)n:F1} rooms on average");
        // a ruin has fewer roofs than a whole building, a split one is wider than the same wing whole
        int roofsIntact = 0, roofsRuin = 0, total = 0;
        for (int i = 0; i < 80; i++)
        {
            var a = PlanGen.Building(1000 + i, BuildingKind.Albergo, BuildingState.Intact);
            var b = PlanGen.Building(1000 + i, BuildingKind.Albergo, BuildingState.Collapse);
            if (a == null || b == null) continue;
            total++;
            roofsIntact += a.rooms.FindAll(r => r.ruined).Count * 100 / Math.Max(1, a.rooms.Count);
            roofsRuin += b.rooms.FindAll(r => r.ruined).Count * 100 / Math.Max(1, b.rooms.Count);
        }
        Check("BUILDING_COLLAPSE_LOSES_ROOFS", total > 0 && roofsRuin > roofsIntact + total * 30, $"{(total > 0 ? roofsIntact / total : 0)}% roofless whole, {(total > 0 ? roofsRuin / total : 0)}% collapsing");
        int splitApart = 0, splitTotal = 0, wholeApart = 0;
        foreach (var kind in new[] { BuildingKind.Albergo, BuildingKind.Chiesa, BuildingKind.Complesso, BuildingKind.Teatro, BuildingKind.Sanatorio })
            for (int i = 0; i < 24; i++)
            {
                var whole = PlanGen.Building(2000 + i, kind, BuildingState.Intact);
                var split = PlanGen.Building(2000 + i, kind, BuildingState.Split);
                if (whole == null || split == null) continue;
                splitTotal++;
                if (Components(split) > 1) splitApart++;
                if (Components(whole) > 1) wholeApart++;
            }
        Check("BUILDING_SPLIT_DRIFTS_APART", splitTotal > 0 && splitApart > splitTotal * 0.6f && wholeApart == 0, $"{splitApart} of {splitTotal} split buildings are in separate pieces ({wholeApart} whole ones are)");
    }

    /// <summary>How many pieces a plan is made of, joining the rooms that share a stretch of wall.</summary>
    static int Components(Plan p)
    {
        var parent = new int[p.rooms.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
        foreach (var room in p.rooms)
            foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                foreach (var seg in p.Segments(room, s))
                    if (seg.other >= 0) parent[Find(room.id)] = Find(seg.other);
        int n = 0;
        for (int i = 0; i < parent.Length; i++) if (Find(i) == i) n++;
        return n;
    }

    // ------------------------------------------------------------------ the stacks
    static void Stacks(int count)
    {
        int bad = 0, flags = 0, align = 0, unreachable = 0, noEntrance = 0, noBoss = 0;
        string first = "";
        var rng = new Random(5);
        for (int i = 0; i < count; i++)
        {
            int levels = 2 + i % 13;
            bool church = i % 3 == 0;
            var theme = i % 2 == 0 ? Theme.Albergo : Theme.Sanatorio;
            bool tower = i % 4 != 3;
            int entrance = tower ? 0 : levels - 1, boss = tower ? levels - 1 : 0;
            int seed = rng.Next(1, 1 << 28);
            Plan stairAt = null;
            for (int l = 0; l < levels; l++)
            {
                var p = PlanGen.StackLevel(seed, l, levels, entrance, boss, theme, church, false);
                string why = PlanGen.BuildingFault(p);
                if (why != null) { bad++; if (first.Length == 0) first = why; }
                var stair = PlanGen.StairRoom(p);
                if (stair.noFloor != (l > 0) || stair.noCeil != (l < levels - 1)) flags++;
                if (stair.x0 != 0 || stair.z0 != 0 || stair.W != PlanGen.StairW || stair.D != PlanGen.StairD) align++;
                if (stairAt != null && (stairAt.rooms[0].x0 != stair.x0 || stairAt.rooms[0].z0 != stair.z0)) align++;
                stairAt = p;
                var hops = p.Hops(0);
                foreach (int h in hops) if (h < 0) { unreachable++; break; }
                bool door = false;
                foreach (var o in p.openings) if (o.b < 0 && o.sill < 0.01f && o.a == 1) door = true;
                if (l == entrance && !door) noEntrance++;
                if ((l == boss) != (PlanGen.HallRoom(p).type == RoomType.Boss)) noBoss++;
            }
        }
        Check("STACK_LEVELS_VALID", bad == 0, bad == 0 ? "every level passes BuildingFault" : bad + " faults, first: " + first);
        Check("STACK_STAIR_ROOM_FLAGS", flags == 0, "no floor above the first level, no ceiling below the last");
        Check("STACK_STAIR_ROOM_ALIGNED", align == 0, "the stair room is at the same place and size on every level");
        Check("STACK_EVERY_ROOM_REACHABLE", unreachable == 0, "all seven rooms of a level are reachable from the stair room");
        Check("STACK_ENTRANCE_DOOR", noEntrance == 0, "the entrance level has a door to the sand");
        Check("STACK_BOSS_HALL_MARKED", noBoss == 0, "only the boss level's hall is of type Boss");
    }

    // ------------------------------------------------------------------ the world of sites
    static void World(int radius)
    {
        DesertWorld.Reset(20261118);
        var counts = new Dictionary<SiteKind, int>();
        var states = new Dictionary<BuildingState, int>();
        var all = new List<Site>();
        var sw = Stopwatch.StartNew();
        for (int cx = -radius; cx <= radius; cx++)
            for (int cz = -radius; cz <= radius; cz++)
            {
                var s = DesertWorld.SiteOf(cx, cz);
                if (s == null) continue;
                all.Add(s);
                counts[s.kind] = (counts.TryGetValue(s.kind, out int c) ? c : 0) + 1;
                states[s.state] = (states.TryGetValue(s.state, out int d) ? d : 0) + 1;
            }
        sw.Stop();
        int cells = (2 * radius + 1) * (2 * radius + 1);
        Check("WORLD_SITE_DENSITY", all.Count > cells * 0.45f && all.Count < cells * 0.75f, $"{all.Count} sites in {cells} cells, made in {sw.ElapsedMilliseconds} ms");
        foreach (var kv in counts) Console.WriteLine($"     {kv.Key,-10} {kv.Value,4}");
        foreach (var kv in states) Console.WriteLine($"     state {kv.Key,-9} {kv.Value,4}");
        bool inCell = true, noOverlap = true, clearOfCathedral = true, padsOk = true;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s.fx0 < s.cx * DesertWorld.Cell + 12f || s.fx1 > (s.cx + 1) * DesertWorld.Cell - 12f || s.fz0 < s.cz * DesertWorld.Cell + 12f || s.fz1 > (s.cz + 1) * DesertWorld.Cell - 12f) inCell = false;
            if (DesertWorld.DistToCathedral(s.CentreX, s.CentreZ) < 100f) clearOfCathedral = false;
            for (int j = i + 1; j < all.Count; j++)
            {
                var t = all[j];
                if (s.fx0 < t.fx1 + 20f && t.fx0 < s.fx1 + 20f && s.fz0 < t.fz1 + 20f && t.fz0 < s.fz1 + 20f) noOverlap = false;
            }
            // the ground under a site is flat at its pad height
            float cxm = (s.fx0 + s.fx1) * 0.5f, czm = (s.fz0 + s.fz1) * 0.5f;
            if (Math.Abs(DesertWorld.Height(cxm, czm) - (s.padY - 0.08f)) > 0.01f || Math.Abs(DesertWorld.Height(s.fx0 + 1f, s.fz1 - 1f) - (s.padY - 0.08f)) > 0.01f) padsOk = false;
        }
        Check("WORLD_SITES_INSIDE_THEIR_CELL", inCell, "every footprint keeps 12 m from the edge of its cell");
        Check("WORLD_SITES_DO_NOT_OVERLAP", noOverlap, "no two footprints within 20 m of each other");
        Check("WORLD_CLEAR_AROUND_CATHEDRAL", clearOfCathedral, "no site within 100 m of the cathedral");
        Check("WORLD_PADS_ARE_FLAT", padsOk, "the sand under every footprint is at its pad height");
        var welcome = DesertWorld.SiteOf(0, -1);
        var landmark = DesertWorld.SiteOf(0, -2);
        Check("WORLD_WELCOME_CHAPEL", welcome != null && welcome.kind == SiteKind.Cappella && welcome.state == BuildingState.Ruin, welcome == null ? "missing" : welcome.ToString());
        Check("WORLD_LANDMARK_TOWER", landmark != null && landmark.IsTower && landmark.LevelCount >= 7, landmark == null ? "missing" : landmark.ToString());
        int stacks = 0, tallest = 0, bossOk = 0;
        foreach (var s in all)
        {
            if (!s.IsStack) continue;
            stacks++;
            tallest = Math.Max(tallest, s.LevelCount);
            if (s.IsTower ? s.boss == s.LevelCount - 1 && s.entrance == 0 : s.boss == 0 && s.entrance == s.LevelCount - 1) bossOk++;
        }
        Check("WORLD_BOSSES_AT_THE_END", stacks > 0 && bossOk == stacks, $"{stacks} towers and catacombs, tallest {tallest} levels ({tallest * PlanGen.LevelHeight:F0} m)");
        Check("WORLD_TOWERS_TALLER_THAN_THE_GAME", tallest > 8, $"a tower of {tallest} floors (the standard branches have 3 levels each)");
        // the same seed makes the same world
        var again = new List<string>();
        DesertWorld.Reset(20261118);
        for (int cx = -3; cx <= 3; cx++) for (int cz = -3; cz <= 3; cz++) { var s = DesertWorld.SiteOf(cx, cz); again.Add(s == null ? "-" : s.ToString()); }
        var first = new List<string>();
        DesertWorld.Reset(20261118);
        for (int cx = -3; cx <= 3; cx++) for (int cz = -3; cz <= 3; cz++) { var s = DesertWorld.SiteOf(cx, cz); first.Add(s == null ? "-" : s.ToString()); }
        Check("WORLD_REPRODUCIBLE", string.Join("|", again) == string.Join("|", first), "two worlds with the same seed list the same sites");
    }

    // ------------------------------------------------------------------ the way home, said in words
    static void Compass()
    {
        bool good = true; string bad = "";
        for (int h = 0; h < 24; h++)
        {
            double th = h * Math.PI / 12 + 0.07, fx = Math.Sin(th), fz = Math.Cos(th);      // the heading, clockwise from +z
            for (int k = 0; k < 8; k++)
            {
                double a = th + k * Math.PI / 4 + (k % 2 == 0 ? 0.2 : -0.2);                // the target: k times 45 degrees to the right of the heading, a little off
                int s = DesertWorld.Sector((float)(Math.Sin(a) * 120.0), (float)(Math.Cos(a) * 120.0), (float)fx, (float)fz);
                if (s != k) { good = false; if (bad.Length < 90) bad += $" (heading {h}, sector {k} gave {s})"; }
            }
        }
        Check("COMPASS_SECTORS", good, good ? "24 headings x 8 targets: each lands in its own sector of 45 degrees (0 ahead, 2 right, 4 behind, 6 left)" : bad);
        Check("COMPASS_UNDEFINED", DesertWorld.Sector(0f, 0f, 0f, 1f) == -1 && DesertWorld.Sector(5f, 5f, 0f, 0f) == -1, "no sector when the target is on the spot or the look is undefined");
        DesertWorld.CathedralCentre(out float cx, out float cz);
        Check("COMPASS_CATHEDRAL_CENTRE", DesertWorld.DistToCathedral(cx, cz) < 0.001f, $"the landmark the HUD points to is inside the cathedral ({cx:F1}, {cz:F1})");
    }
}
