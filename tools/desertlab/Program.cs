using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PryingEyes.Deserto;
using PryingEyes.Proc;

/// <summary>Checks of the desert's pure code (ground, sites, clock) and of the building and stack generators, with no Unity: tools/desertlab.
/// Usage: dotnet run --project tools/desertlab [-- --quick] [--report FILE]. --report writes every check as JSON (the studio's `test desert` reads it).</summary>
static class Program
{
    static bool ok = true;
    // Every check, kept for --report in the shape of the other labs: code, subject (the group that ran it), passed, observed, expected.
    static readonly List<object> findings = new List<object>();
    static string subject = "desert";
    static void Check(string code, bool passed, string observed, string expected = "")
    {
        ok &= passed;
        findings.Add(new { code, subject, passed, observed, expected });
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {code}: {observed}");
    }

    static int Main(string[] args)
    {
        int map = Array.IndexOf(args, "--map");
        if (map >= 0) return Map(args, map);
        int mini = Array.IndexOf(args, "--minimap");
        if (mini >= 0) return Minimap(args, mini);
        int probe = Array.IndexOf(args, "--probe");
        if (probe >= 0) return Probe(args, probe);
        bool quick = Array.IndexOf(args, "--quick") >= 0;
        int report = Array.IndexOf(args, "--report");
        if (report >= 0 && report + 1 >= args.Length) { Console.Error.WriteLine("--report needs a file name"); return 2; }
        subject = "noise"; Noise();
        subject = "clock"; Clock();
        subject = "ground"; Ground();
        subject = "lakes"; Lakes();
        subject = "biomes"; Biomes();
        subject = "terrain"; Terrain();
        subject = "buildings"; Buildings(quick ? 120 : 600);
        subject = "stacks"; Stacks(quick ? 40 : 200);
        subject = "world"; World(quick ? 9 : 20);
        subject = "map"; MapPaint();
        subject = "regions"; Regions();
        subject = "buttes"; Buttes();
        subject = "scatter"; Scatter(quick ? 14 : 30);
        subject = "yards"; Yards(quick ? 10 : 20);
        subject = "hub"; Hub();
        subject = "weather"; Weather();
        subject = "grading"; Grading();
        subject = "compass"; Compass();
        Console.WriteLine(ok ? "ALL PASS" : "FAILED");
        if (report >= 0)
            File.WriteAllText(args[report + 1], JsonSerializer.Serialize(new { schemaVersion = 1, operation = "desert.rules", ok, findings }, new JsonSerializerOptions { WriteIndented = true }));
        return ok ? 0 : 1;
    }

    // ------------------------------------------------------------------ a picture of the ground
    /// <summary>--map out.ppm x z size pixels [seed]: a hill-shaded picture of the ground (the sun low in the north-west) with the sites as dots, to look at the landforms without Unity.
    /// The colour is the region's sand, rock where the ground is steep; red dots are the sites.</summary>
    static int Map(string[] a, int i)
    {
        string path = a[i + 1];
        float x0 = float.Parse(a[i + 2], System.Globalization.CultureInfo.InvariantCulture), z0 = float.Parse(a[i + 3], System.Globalization.CultureInfo.InvariantCulture);
        float size = float.Parse(a[i + 4], System.Globalization.CultureInfo.InvariantCulture); int px = int.Parse(a[i + 5]);
        DesertWorld.Reset(a.Length > i + 6 ? int.Parse(a[i + 6]) : 1000);
        bool heights = a.Length > i + 7 && a[i + 7] == "h";
        float step = size / px;
        var h = new float[px + 2, px + 2];
        for (int r = 0; r < px + 2; r++)
            for (int c = 0; c < px + 2; c++) h[c, r] = DesertWorld.Height(x0 + (c - 1) * step, z0 + (r - 1) * step);
        var bytes = new byte[px * px * 3];
        float[] lightDir = { -0.6f, 0.45f, 0.66f };
        for (int r = 0; r < px; r++)
            for (int c = 0; c < px; c++)
            {
                float dx = (h[c + 2, r + 1] - h[c, r + 1]) / (2f * step), dz = (h[c + 1, r + 2] - h[c + 1, r]) / (2f * step);
                float nx = -dx, ny = 1f, nz = -dz, len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                nx /= len; ny /= len; nz /= len;
                float shade = Math.Max(0f, nx * lightDir[0] + ny * lightDir[1] + nz * lightDir[2]);
                float slope = (float)Math.Sqrt(dx * dx + dz * dz);
                DesertWorld.TintAt(x0 + c * step, z0 + r * step, out float tr, out float tg, out float tb, out float des);
                float rock = Math.Min(1f, Math.Max(0f, (slope - 0.6f) / 0.5f));
                float cr = 0.80f * tr, cg = 0.64f * tg, cb = 0.42f * tb;
                cr = cr * (1f - rock) + 0.45f * rock; cg = cg * (1f - rock) + 0.33f * rock; cb = cb * (1f - rock) + 0.24f * rock;
                float lum = 0.3f * cr + 0.59f * cg + 0.11f * cb;
                cr += (lum - cr) * des; cg += (lum - cg) * des; cb += (lum - cb) * des;
                float k = 0.28f + 0.95f * shade;
                int o = (r * px + c) * 3;
                bytes[o] = (byte)Math.Min(255, cr * k * 255f); bytes[o + 1] = (byte)Math.Min(255, cg * k * 255f); bytes[o + 2] = (byte)Math.Min(255, cb * k * 255f);
                if (heights)
                {
                    // elevation: a colour ramp every 40 m of height above the flat sand, with a contour line every 4 m, over the shading
                    float e = h[c + 1, r + 1] - DesertWorld.GroundY;
                    float t = Math.Min(1f, e / 40f);
                    float rr = t < 0.5f ? 0.15f + 1.2f * t : 0.75f + 0.5f * (t - 0.5f), gg = t < 0.5f ? 0.35f + 1.0f * t : 0.85f - 1.4f * (t - 0.5f), bb = t < 0.5f ? 0.60f - 1.1f * t : 0.05f + 0.3f * (t - 0.5f);
                    float cont = Math.Abs(e / 4f - (float)Math.Round(e / 4f)) < 0.5f * step / 4f * Math.Max(0.5f, slope * 2f) ? 0.55f : 1f;
                    float sh = 0.55f + 0.6f * shade;
                    bytes[o] = (byte)Math.Min(255, rr * sh * cont * 255f); bytes[o + 1] = (byte)Math.Min(255, gg * sh * cont * 255f); bytes[o + 2] = (byte)Math.Min(255, bb * sh * cont * 255f);
                }
            }
        var list = new List<Site>();
        DesertWorld.SitesNear(x0 + size * 0.5f, z0 + size * 0.5f, size * 0.75f, list);
        foreach (var s in list)
        {
            int cx = (int)((s.CentreX - x0) / step), cz = (int)((s.CentreZ - z0) / step);
            for (int dz2 = -1; dz2 <= 1; dz2++)
                for (int dx2 = -1; dx2 <= 1; dx2++)
                {
                    int c = cx + dx2, r = cz + dz2;
                    if (c < 0 || r < 0 || c >= px || r >= px) continue;
                    int o = (r * px + c) * 3; bytes[o] = 230; bytes[o + 1] = 30; bytes[o + 2] = 30;
                }
        }
        using (var f = System.IO.File.Create(path))
        {
            var head = System.Text.Encoding.ASCII.GetBytes("P6\n" + px + " " + px + "\n255\n");
            f.Write(head, 0, head.Length); f.Write(bytes, 0, bytes.Length);
        }
        Console.WriteLine($"{path}: {px}x{px}, {size} m, from ({x0}, {z0}), seed {DesertWorld.Seed}");
        return 0;
    }

    /// <summary>--probe x0 z0 x1 z1 [n] [seed]: the ground and the regions along a line (height, the two regions that meet, how much the first counts, the sand tint), to see what a picture shows.</summary>
    static int Probe(string[] a, int i)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        float x0 = float.Parse(a[i + 1], inv), z0 = float.Parse(a[i + 2], inv), x1 = float.Parse(a[i + 3], inv), z1 = float.Parse(a[i + 4], inv);
        int n = a.Length > i + 5 ? int.Parse(a[i + 5]) : 20;
        DesertWorld.Reset(a.Length > i + 6 ? int.Parse(a[i + 6]) : 1000);
        for (int k = 0; k <= n; k++)
        {
            float t = (float)k / n, x = x0 + (x1 - x0) * t, z = z0 + (z1 - z0) * t;
            DesertWorld.RegionBlend(x, z, out var ra, out var rb, out float wa);
            DesertWorld.TintAt(x, z, out float r, out float g, out float b, out float d);
            Console.WriteLine(string.Format(inv, "({0,7:F1},{1,7:F1}) h {2,6:F2}  {3}/{4} {5:F2}  tint {6:F2} {7:F2} {8:F2} desat {9:F2}", x, z, DesertWorld.Height(x, z), ra, rb, wa, r, g, b, d));
        }
        return 0;
    }

    // ------------------------------------------------------------------ the minimap
    /// <summary>--minimap out.ppm cx cz [size] [seed]: the minimap of the game (DesertMapGen, 2 m a pixel) around a point, north up, to look at the paper without Unity.</summary>
    static int Minimap(string[] a, int i)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string path = a[i + 1];
        float cx = float.Parse(a[i + 2], inv), cz = float.Parse(a[i + 3], inv), size = a.Length > i + 4 ? float.Parse(a[i + 4], inv) : DesertMapGen.WindowSize;
        DesertWorld.Reset(a.Length > i + 5 ? int.Parse(a[i + 5]) : 1000);
        int t0x = DesertMapGen.TileOf(cx - size * 0.5f), t1x = DesertMapGen.TileOf(cx + size * 0.5f - 0.01f);
        int t0z = DesertMapGen.TileOf(cz - size * 0.5f), t1z = DesertMapGen.TileOf(cz + size * 0.5f - 0.01f);
        int T = DesertMapGen.TilePx, W = (t1x - t0x + 1) * T, H = (t1z - t0z + 1) * T;
        var img = new byte[W * H * 3];
        var tile = new byte[T * T * 4];
        var list = new List<Site>();
        var sw = Stopwatch.StartNew();
        int n = 0;
        for (int tz = t0z; tz <= t1z; tz++)
            for (int tx = t0x; tx <= t1x; tx++)
            {
                DesertWorld.SitesNear((tx + 0.5f) * DesertMapGen.TileSize, (tz + 0.5f) * DesertMapGen.TileSize, DesertMapGen.SiteReach, list);
                DesertMapGen.Paint(tx, tz, list, tile);
                n++;
                for (int j = 0; j < T; j++)
                    for (int c = 0; c < T; c++)
                    {
                        int X = (tx - t0x) * T + c, Y = H - 1 - ((tz - t0z) * T + j);
                        int o = (Y * W + X) * 3, s = (j * T + c) * 4;
                        img[o] = tile[s]; img[o + 1] = tile[s + 1]; img[o + 2] = tile[s + 2];
                    }
            }
        using (var f = System.IO.File.Create(path))
        {
            var head = System.Text.Encoding.ASCII.GetBytes("P6\n" + W + " " + H + "\n255\n");
            f.Write(head, 0, head.Length); f.Write(img, 0, img.Length);
        }
        Console.WriteLine($"{path}: {W}x{H} px, {n} tiles in {sw.ElapsedMilliseconds} ms ({(double)sw.ElapsedMilliseconds / n:F1} ms each), centre ({cx}, {cz}), seed {DesertWorld.Seed}");
        return 0;
    }

    static int Dark(byte[] px, int from = 0) { int n = 0; for (int k = from; k < px.Length; k += 4) if (px[k] < 70 && px[k + 1] < 70 && px[k + 2] < 70) n++; return n; }

    /// <summary>The pure painter of the minimap: same tile, same bytes; opaque; the cathedral, its golden stairs, a chapel and a tower show; the cost of a tile.</summary>
    static void MapPaint()
    {
        DesertWorld.Reset(1000);
        int T = DesertMapGen.TilePx;
        var a = new byte[T * T * 4]; var b = new byte[T * T * 4]; var list = new List<Site>();
        void PaintAt(int tx, int tz, byte[] into)
        {
            DesertWorld.SitesNear((tx + 0.5f) * DesertMapGen.TileSize, (tz + 0.5f) * DesertMapGen.TileSize, DesertMapGen.SiteReach, list);
            DesertMapGen.Paint(tx, tz, list, into);
        }
        PaintAt(-3, 4, a); PaintAt(-3, 4, b);
        Check("MAP_TILE_IS_DETERMINISTIC", a.AsSpan().SequenceEqual(b), "the same tile painted twice gives the same bytes");
        bool opaque = true; int tiles = 0; var sw = Stopwatch.StartNew();
        for (int tz = -2; tz <= 2; tz++)
            for (int tx = -2; tx <= 2; tx++)
            {
                PaintAt(tx, tz, a); tiles++;
                for (int k = 3; k < a.Length; k += 4) if (a[k] != 255) opaque = false;
            }
        double ms = (double)sw.ElapsedMilliseconds / tiles;
        Check("MAP_TILES_ARE_OPAQUE", opaque, tiles + " tiles around the cathedral, every pixel alpha 255");
        Check("MAP_TILE_COST", ms < 60.0, $"{ms:F1} ms a tile on one thread (the game paints them on workers, three at a time)");
        // the cathedral: pale stone under a cross, and the stairs in gold
        PaintAt(0, 0, a);
        int stone = 0;
        for (int k = 0; k < a.Length; k += 4) if (Math.Abs(a[k] - 214) <= 4 && Math.Abs(a[k + 1] - 207) <= 4 && Math.Abs(a[k + 2] - 189) <= 4) stone++;
        Check("MAP_CATHEDRAL_IS_STONE", stone >= 150, stone + " pale stone pixels in the tile of the cathedral (a nave, aisles, tower and armoury of about 2000 m2 are 500 pixels)");
        Check("MAP_CATHEDRAL_HAS_ITS_WALLS", Dark(a) >= 100, Dark(a) + " ink pixels in the tile of the cathedral");
        PaintAt(0, -1, a);
        int gold = 0;
        for (int j = 0; j < T; j++)
            for (int c = 0; c < T; c++)
            {
                float wx = (c + 0.5f) * DesertMapGen.Mpp, wz = -DesertMapGen.TileSize + (j + 0.5f) * DesertMapGen.Mpp;
                int o = (j * T + c) * 4;
                if (wx >= HubPlan.StairX0 && wx <= HubPlan.StairX1 && wz >= -6f && wz <= 0f && a[o] > 190 && a[o] > a[o + 1] + 25 && a[o + 1] > a[o + 2] + 40) gold++;
            }
        Check("MAP_STAIRS_ARE_GOLD", gold >= 14, gold + " golden pixels on the stairs of the churchyard (14 m x 6 m: 21 pixels)");
        // a ruined chapel and the tower on the horizon
        var chapel = DesertWorld.Welcome(); var tower = DesertWorld.Landmark();
        PaintAt(DesertMapGen.TileOf(chapel.CentreX), DesertMapGen.TileOf(chapel.CentreZ), a);
        Check("MAP_CHAPEL_SHOWS", Dark(a) >= 25, Dark(a) + " ink pixels where the welcome chapel stands");
        PaintAt(DesertMapGen.TileOf(tower.CentreX), DesertMapGen.TileOf(tower.CentreZ), a);
        Check("MAP_TOWER_SHOWS", Dark(a) >= 40, Dark(a) + " ink pixels where the landmark tower stands");
        // the sand away from everything: no ink at all, only paper and contour lines
        PaintAt(40, 40, a);
        Check("MAP_BARE_SAND_HAS_NO_INK", Dark(a) == 0 || list.Count > 0, "far sand: " + Dark(a) + " ink pixels, " + list.Count + " sites near");
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
    static void Lakes()
    {
        int failures = 0;
        float shallowestVariation = float.PositiveInfinity;
        for (int seed = 960; seed < 1040; seed++)
        {
            DesertWorld.Reset(seed);
            if (DesertWorld.Lakes.Count != 2) failures++;
            foreach (var lake in DesertWorld.Lakes)
            {
                float shortest = float.PositiveInfinity, longest = 0f;
                if (!DesertWorld.LakeAt(lake.X, lake.Z, out _, out float depth) || depth < 7.5f) failures++;
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * (float)(Math.PI / 6);
                    var shore = lake.PointAt(angle, 1f);
                    float dx = (shore.x - lake.X) / lake.RadiusX, dz = (shore.z - lake.Z) / lake.RadiusZ;
                    float reach = (float)Math.Sqrt(dx * dx + dz * dz);
                    shortest = Math.Min(shortest, reach);
                    longest = Math.Max(longest, reach);
                    var outside = lake.PointAt(angle, 1.08f);
                    var inside = lake.PointAt(angle, 0.75f);
                    if (Math.Abs(DesertWorld.Height(shore.x, shore.z) - lake.Surface) > 0.01f ||
                        DesertWorld.Height(outside.x, outside.z) <= lake.Surface ||
                        DesertWorld.LakeAt(outside.x, outside.z, out _, out _) ||
                        !DesertWorld.LakeAt(inside.x, inside.z, out _, out depth) ||
                        Math.Abs(DesertWorld.Height(inside.x, inside.z) + depth - lake.Surface) > 0.01f)
                        failures++;
                }
                shallowestVariation = Math.Min(shallowestVariation, longest - shortest);
            }
        }
        Check("LAKES_SHARE_A_WALKABLE_SHORE_WITH_TERRAIN", failures == 0, $"{failures} mismatches across 80 seeds");
        Check("LAKES_HAVE_COVES_AND_HEADLANDS", shallowestVariation > 0.24f, $"minimum variation in normalized shore reach {shallowestVariation:F2}");
    }

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
                worst = Math.Max(worst, slope); tall = Math.Max(tall, h0);
                if (!DesertWorld.LakeAt(x, z, out _, out _)) low = Math.Min(low, h0);
                if (slope > 0.9f) steep++;
                n++;
            }
        Check("DRY_GROUND_NOT_BELOW_SEA", low >= DesertWorld.GroundY - 0.2f, $"lowest dry point {low:F2} m (sea level {DesertWorld.GroundY} m)");
        Check("GROUND_MOSTLY_WALKABLE", steep < n * 0.02f, $"{steep} of {n} samples steeper than 42 degrees (worst {Math.Atan(worst) * 57.3f:F0} deg), highest {tall:F1} m");
        // Compare exposed dunes near the cathedral with the dune fields in the southern desert.
        float nearMax = 0f, farMax = 0f;
        for (float x = -410f; x <= 410f; x += 13f)
            for (float z = -410f; z <= 410f; z += 13f)
                if (DesertBiomes.At(x, z, DesertWorld.Seed).Desert >= 0.999f)
                    nearMax = Math.Max(nearMax, DesertWorld.Raw(x, z) - DesertWorld.Buttes(x, z) - DesertWorld.GroundY);
        for (float x = -2100f; x <= 2100f; x += 23f)
            for (float z = -9000f; z <= -5000f; z += 27f)
                if (DesertWorld.RegionWeight(Region.Dunes, x, z) >= 0.99f)
                    farMax = Math.Max(farMax, DesertWorld.Raw(x, z) - DesertWorld.Buttes(x, z) - DesertWorld.GroundY);
        Check("GROUND_DUNES_GROW_WITH_DISTANCE", farMax > nearMax * 1.2f, $"{nearMax:F1} m near, {farMax:F1} m far");
        // the dunes have a long gentle windward face and a steep slip face: along the wind (u) the sand rises slowly and falls fast
        double up = 0, down = 0; int nu = 0, nd = 0, cliffs = 0, badSamples = 0;
        for (float x = -7000f; x <= 7000f; x += 41.3f)
            for (float z = -7000f; z <= 7000f; z += 43.7f)
            {
                if (DesertWorld.DistToCathedral(x, z) < 500f) continue;
                if (DesertBiomes.At(x, z, DesertWorld.Seed).Desert < 0.99f) continue;
                if (DesertWorld.RegionWeight(Region.Dunes, x, z) > 0.99f)
                {
                    float s0 = (DesertWorld.Height(x + 2.2f, z + 1.25f) - DesertWorld.Height(x - 2.2f, z - 1.25f)) / 5.06f;       // along the wind (0.87, 0.5)
                    if (s0 > 0.04f) { up += s0; nu++; } else if (s0 < -0.04f) { down -= s0; nd++; }
                }
                if (DesertWorld.RegionWeight(Region.Badlands, x, z) > 0.99f)
                {
                    badSamples++;
                    float g = Math.Max(Math.Abs(DesertWorld.Height(x + 2f, z) - DesertWorld.Height(x, z)), Math.Abs(DesertWorld.Height(x, z + 2f) - DesertWorld.Height(x, z))) / 2f;
                    if (g > 1.0f) cliffs++;
                }
            }
        double meanUp = nu > 0 ? up / nu : 0, meanDown = nd > 0 ? down / nd : 0;
        Check("GROUND_DUNES_HAVE_SLIP_FACES", nu > 200 && nd > 200 && meanDown > meanUp * 1.25, $"along the wind the sand rises at {meanUp:F2} on average ({nu} samples) and falls at {meanDown:F2} ({nd} samples): {(meanUp > 0 ? meanDown / meanUp : 0):F2} times steeper");
        Check("GROUND_BADLANDS_HAVE_CLIFFS", badSamples > 1000 && cliffs >= 8 && cliffs < badSamples * 0.15, $"{cliffs} of {badSamples} badlands samples are steeper than 45 degrees (walls of mesas, not most of the land)");

        // the brine on the salt flats: a film of water in lakes, only in the middle of the flats, riding on the top fifth of the vertex alpha
        DesertWorld.Reset(12345);
        int flats = 0, flooded = 0, stray = 0, badAlpha = 0, badDecode = 0, brineSamples = 0;
        for (float x = -9000f; x <= 9000f; x += 31.1f)
            for (float z = -9000f; z <= 9000f; z += 36.3f)
            {
                float bn = DesertWorld.Brine(x, z), sw = DesertWorld.RegionWeight(Region.Salt, x, z);
                if (bn > 0f && sw < 0.9f) stray++;
                if (sw > 0.99f && DesertBiomes.At(x, z, DesertWorld.Seed).Desert > 0.99f) { flats++; if (bn > 0.5f) flooded++; }
                DesertWorld.TintAt(x, z, out _, out _, out _, out float td);
                float al = DesertWorld.BrineAlpha(x, z, td);
                if (bn <= 0f) { if (Math.Abs(al - td) > 1e-6f) badAlpha++; }
                else
                {
                    brineSamples++;
                    if (al < 0.8f || al > 1.0001f) badAlpha++;
                    if (sw > 0.99f && Math.Abs((al - 0.8f) * 5f - bn) > 0.01f) badDecode++;
                }
            }
        Check("BRINE_ONLY_IN_THE_FLATS", stray == 0 && brineSamples > 500, $"{stray} samples with water where the flats are less than 90 % of the ground, {brineSamples} samples with water in all");
        Check("BRINE_COVERS_ABOUT_A_THIRD_OF_THE_FLATS", flats > 2000 && flooded > flats * 0.15f && flooded < flats * 0.40f, $"{flooded} of {flats} samples in the middle of the flats are more than half under water ({100.0 * flooded / Math.Max(1, flats):F0} %)");
        Check("BRINE_RIDES_ON_THE_TINT_ALPHA", badAlpha == 0 && badDecode == 0, $"{badAlpha} vertices whose alpha does not say what the water does (dry ones keep the region's alpha, wet ones sit between 0.8 and 1), {badDecode} where the shader would decode another depth");
        // the same seed gives the same lakes, whenever and in whatever order they are asked for (a worker thread makes the sand)
        int moved = 0;
        var wet = new List<(float x, float z, float b)>();
        for (float x = -9000f; x <= 9000f && wet.Count < 40; x += 211f)
            for (float z = -9000f; z <= 9000f && wet.Count < 40; z += 233f)
            {
                float bn = DesertWorld.Brine(x, z);
                if (bn > 0.5f) wet.Add((x, z, bn));
            }
        DesertWorld.Reset(999);
        DesertWorld.Reset(12345);
        for (int k = wet.Count - 1; k >= 0; k--) if (DesertWorld.Brine(wet[k].x, wet[k].z) != wet[k].b) moved++;
        Check("BRINE_DETERMINISTIC", wet.Count >= 10 && moved == 0, $"{wet.Count} wet places asked again after another seed and a reset, in reverse order: {moved} changed");
    }

    static void Biomes()
    {
        bool reachable = true, smooth = true;
        float smallestRidge = float.PositiveInfinity, smallestMeadow = float.PositiveInfinity;
        foreach (int seed in new[] { 1, 84, 1000, 1017, 431792, -3 })
        {
            DesertWorld.Reset(seed);
            foreach (Biome kind in Enum.GetValues(typeof(Biome)))
            {
                var (x, z) = DesertBiomes.Landmark(kind);
                reachable &= DesertBiomes.At(x, z, seed).Dominant == kind;
                if (kind == Biome.Desert) continue;
                BiomeMix previous = default;
                for (int i = 0; i <= 100; i++)
                {
                    float t = i / 100f;
                    var now = DesertBiomes.At(x * t, z * t, seed);
                    float sum = now.Desert + now.Forest + now.Meadow + now.Ice;
                    smooth &= Math.Abs(1f - sum) < 0.0001f && Math.Abs(now.Desert - previous.Desert) < 0.12f &&
                        Math.Abs(now.Forest - previous.Forest) < 0.12f && Math.Abs(now.Ice - previous.Ice) < 0.12f &&
                        Math.Abs(now.Meadow - previous.Meadow) < 0.12f;
                    previous = now;
                }
            }
            foreach (var lake in DesertWorld.Lakes)
                reachable &= DesertBiomes.At(lake.X, lake.Z, seed).Dominant == Biome.Desert;
            var (iceX, iceZ) = DesertBiomes.Landmark(Biome.Ice);
            float iceFloor = DesertWorld.Raw(iceX, iceZ), peak = iceFloor;
            for (float z = 1080f; z <= 1400f; z += 22f)
                for (float x = -155f; x <= 270f; x += 24f)
                    peak = Math.Max(peak, DesertWorld.Raw(x, z));
            smallestRidge = Math.Min(smallestRidge, peak - iceFloor);
            float meadowLow = float.PositiveInfinity, meadowHigh = float.NegativeInfinity;
            for (float x = -1000f; x < -620f; x += 23f)
                for (float z = -130f; z <= 290f; z += 28f)
                {
                    float h = DesertWorld.Raw(x, z);
                    meadowLow = Math.Min(meadowLow, h);
                    meadowHigh = Math.Max(meadowHigh, h);
                }
            smallestMeadow = Math.Min(smallestMeadow, meadowHigh - meadowLow);
        }
        Check("BIOMES_REACHABLE_AND_CATHEDRAL_STAYS_DESERT", reachable, "all four landmark climates and both desert lakes on six seeds");
        Check("BIOME_BOUNDARIES_BLEND", smooth, "weights stay normalized and continuous along three routes from the cathedral");
        Check("BIOMES_HAVE_LANDMARK_RELIEF", smallestRidge > 20f && smallestMeadow > 10f,
            $"at least {smallestRidge:F1} m from the icefield to the ridge and {smallestMeadow:F1} m from meadow valleys to hilltops on all six seeds");

        DesertWorld.Reset(1000);
        var groups = new Dictionary<Biome, int[]>();
        var props = new List<PropItem>();
        foreach (Biome kind in new[] { Biome.Forest, Biome.Ice, Biome.Meadow })
        {
            var (x, z) = DesertBiomes.Landmark(kind);
            int cx = (int)Math.Floor(x / DesertScatterGen.Chunk), cz = (int)Math.Floor(z / DesertScatterGen.Chunk);
            var tally = groups[kind] = new int[(int)Prop.Count];
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    DesertScatterGen.Build(cx + dx, cz + dz, props);
                    foreach (var prop in props) tally[prop.kind]++;
                }
        }
        int trees = groups[Biome.Forest][(int)Prop.Broadleaf] + groups[Biome.Forest][(int)Prop.Fir];
        int ferns = groups[Biome.Forest][(int)Prop.ForestFern];
        int frosted = groups[Biome.Ice][(int)Prop.FrostFir], glaciers = groups[Biome.Ice][(int)Prop.GlacierShard];
        int meadows = groups[Biome.Meadow][(int)Prop.MeadowGrass], flowers = groups[Biome.Meadow][(int)Prop.Wildflowers];
        Check("BIOMES_HAVE_DISTINCT_SCENERY", trees >= 90 && ferns >= 45 && frosted >= 12 && glaciers >= 12 && meadows >= 220 && flowers >= 90,
            $"{trees} woodland trees, {ferns} fern patches, {frosted} snowy trees, {glaciers} glacial rocks, {meadows} grass patches and {flowers} flower patches");
        var forest = DesertTerrainGen.Build(11, 2, 4f, null);
        var next = DesertTerrainGen.Build(12, 2, 4f, null);
        bool seam = true;
        for (int row = 0; row < 17; row++)
            for (int channel = 0; channel < 4; channel++)
                seam &= forest.biome[(row * 17 + 16) * 4 + channel] == next.biome[row * 17 * 4 + channel];
        Check("BIOME_TERRAIN_BORDERS_MATCH", seam && forest.biome[0] > 180, "vertex climate channels meet without seams on adjacent chunks");
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

        // ... and nothing of the chunk hangs inside it either: a well that straddles the edge of two chunks must not have the skirt of that edge across it (in the game the player of the
        // catacomb of cell (0,-2), seed 1000, stopped on the stair landing against that wall of sand). Two synthetic holes, one over an edge and one over a corner, and the real wells.
        int across = 0, wallTris = 0, wallHoles = 0; string wallFirst = "";
        void NoWall(HoleRect r, string what)
        {
            int cx0 = (int)Math.Floor(r.x0 / DesertTerrainGen.ChunkSize), cx1 = (int)Math.Floor((r.x1 - 1e-3f) / DesertTerrainGen.ChunkSize);
            int cz0 = (int)Math.Floor(r.z0 / DesertTerrainGen.ChunkSize), cz1 = (int)Math.Floor((r.z1 - 1e-3f) / DesertTerrainGen.ChunkSize);
            wallHoles++;
            if (cx1 > cx0 || cz1 > cz0) across++;
            foreach (float step in new[] { 1.5f, 4f, 8f })
                for (int px = cx0; px <= cx1; px++)
                    for (int pz = cz0; pz <= cz1; pz++)
                    {
                        var d = DesertTerrainGen.Build(px, pz, step, new[] { r });
                        float ox = px * DesertTerrainGen.ChunkSize, oz = pz * DesertTerrainGen.ChunkSize;
                        for (int t = 0; t < d.tris.Length; t += 3)
                        {
                            int a = d.tris[t] * 3, b = d.tris[t + 1] * 3, c = d.tris[t + 2] * 3;
                            float mx = ox + (d.pos[a] + d.pos[b] + d.pos[c]) / 3f, mz = oz + (d.pos[a + 2] + d.pos[b + 2] + d.pos[c + 2]) / 3f;
                            if (r.Contains(mx, mz)) { wallTris++; if (wallFirst.Length == 0) wallFirst = $"{what}, chunk ({px},{pz}) step {step}"; }
                        }
                    }
        }
        NoWall(new HoleRect(40f, -195f, 46f, -189f), "a hole over the edge z = -192");
        NoWall(new HoleRect(61f, -195f, 67f, -189f), "a hole over the corner (64, -192)");
        for (int cx = -14; cx <= 14; cx++)
            for (int cz = -14; cz <= 14; cz++)
            {
                var s = DesertWorld.SiteOf(cx, cz);
                if (s != null && s.WellRect(out var r)) NoWall(r, s.ToString());
            }
        Check("TERRAIN_NO_WALL_ACROSS_A_WELL", across >= 2 && wallTris == 0, wallTris == 0 ? $"{wallHoles} holes ({across} over a chunk edge) at three steps: no triangle of the sand or of a skirt inside any" : $"{wallTris} triangles inside a hole, first: {wallFirst}");
    }

    // ------------------------------------------------------------------ the building generator
    static void Buildings(int count)
    {
        var rng = new Random(11);
        var kinds = (BuildingKind[])Enum.GetValues(typeof(BuildingKind));
        var states = (BuildingState[])Enum.GetValues(typeof(BuildingState));
        int built = 0, faults = 0, noDoor = 0, tooBig = 0, none = 0, gaps = 0, gapStrays = 0, gapBuildings = 0, lowWholeDoors = 0;
        string firstStray = "";
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
            OpenRoomCensus(p, kind + "/" + state);
            perKind[kind] = (perKind.TryGetValue(kind, out int c) ? c : 0) + 1;
            rooms[kind] = (rooms.TryGetValue(kind, out int r0) ? r0 : 0) + p.rooms.Count;
            string why = PlanGen.BuildingFault(p);
            if (why != null) { faults++; if (firstFault.Length == 0) firstFault = kind + "/" + state + ": " + why; }
            bool door = false;
            foreach (var o in p.openings) if (o.b < 0 && o.sill < 0.01f && !o.oneSided) door = true;
            if (!door) noDoor++;
            // a floor-level opening lower than a person (Opening.Gap) is a doorway whose lintel a lowered wall took away: its head is that wall and a room of it crumbled.
            // Anything else low would be closed from above by a soffit, which the navmesh agent (1.9 m) cannot pass.
            int gapsHere = 0;
            foreach (var o in p.openings)
            {
                if (o.sill >= 0.01f || o.style == OpenStyle.Passaggio || o.style == OpenStyle.Ascensore || o.top >= Opening.WalkTop) continue;
                gapsHere++;
                float hMax = p.rooms[o.a].wallH; bool crumbled = p.rooms[o.a].crumble;
                if (o.b >= 0) { hMax = Math.Min(hMax, p.rooms[o.b].wallH); crumbled |= p.rooms[o.b].crumble; }
                if (!o.Gap || !crumbled || Math.Abs(o.top - hMax) > 0.002f)
                {
                    gapStrays++;
                    if (firstStray.Length == 0) firstStray = $"{kind}/{state}: opening {o.a}-{o.b} head {o.top:F2} m, wall {hMax:F2} m, crumbled {crumbled}";
                }
            }
            gaps += gapsHere;
            if (gapsHere > 0) gapBuildings++;
            if (state == BuildingState.Intact) lowWholeDoors += gapsHere;
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
        Check("BUILDING_LOW_DOORWAYS_ARE_GAPS", gaps > 50 && gapStrays == 0 && lowWholeDoors == 0,
              gapStrays == 0 ? $"{gaps} doorways lower than {Opening.WalkTop} m in {gapBuildings} buildings, every one is a lintel-less gap cut down to a crumbled wall ({lowWholeDoors} in whole buildings)" : $"{gapStrays} low doorways that are not gaps, first: {firstStray}");
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
        int bad = 0, flags = 0, align = 0, unreachable = 0, noEntrance = 0, noBoss = 0, balconies = 0, tallWindows = 0, entryWindows = 0;
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
                OpenRoomCensus(p, $"stack {i} level {l}");
                string why = PlanGen.BuildingFault(p);
                if (why != null) { bad++; if (first.Length == 0) first = why; }
                var stair = PlanGen.StairRoom(p);
                if (stair.noFloor != (l > 0) || stair.noCeil != (tower || l < levels - 1)) flags++;
                var wing = p.rooms[PlanGen.StackRooms - 1];
                int doors = p.openings.FindAll(o => o.a == wing.id && o.b < 0 && !o.alongX && o.sill < 0.01f &&
                    o.line == wing.x1 && o.center == PlanGen.TowerBalconyCenter(wing) && o.noGlass).Count;
                if (doors != (tower && l > 0 ? 1 : 0)) balconies++;
                if (p.openings.Exists(o => o.b < 0 && o.sill > 0.01f && o.top >= p.rooms[o.a].wallH)) tallWindows++;
                int openWindows = p.openings.FindAll(o => o.b < 0 && o.sill == PlanGen.TowerEntrySill && o.noGlass &&
                    o.width >= 16 && o.top - o.sill > 1.5f).Count;
                if (openWindows != (tower ? 3 : 0)) entryWindows++;
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
        Check("STACK_STAIR_ROOM_FLAGS", flags == 0, "stair wells are open between levels and through the tower roof");
        Check("TOWER_BALCONY_DOORS", balconies == 0, "one exterior doorway per upper tower floor");
        Check("STACK_WINDOWS_BELOW_ROOF", tallWindows == 0, "every window stays below the top of its level");
        Check("TOWER_ENTRY_WINDOWS", entryWindows == 0, "three glassless entrances per tower floor");
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
        Check("WORLD_SITE_DENSITY", all.Count > cells * 0.30f && all.Count < cells * 0.85f, $"{all.Count} sites in {cells} cells of {DesertWorld.Cell} m ({all.Count * 100 / cells} %), made in {sw.ElapsedMilliseconds} ms");
        foreach (var kv in counts) Console.WriteLine($"     {kv.Key,-10} {kv.Value,4}");
        foreach (var kv in states) Console.WriteLine($"     state {kv.Key,-9} {kv.Value,4}");
        bool inCell = true, noOverlap = true, clearOfCathedral = true, padsOk = true;
        string whyCell = "", whyPad = "", whyOverlap = "";
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s.fx0 < s.cx * DesertWorld.Cell + 12f || s.fx1 > (s.cx + 1) * DesertWorld.Cell - 12f || s.fz0 < s.cz * DesertWorld.Cell + 12f || s.fz1 > (s.cz + 1) * DesertWorld.Cell - 12f)
            { inCell = false; if (whyCell.Length == 0) whyCell = $" ({s}: x {s.fx0:F1}..{s.fx1:F1} z {s.fz0:F1}..{s.fz1:F1} in the cell x {s.cx * DesertWorld.Cell}..{(s.cx + 1) * DesertWorld.Cell} z {s.cz * DesertWorld.Cell}..{(s.cz + 1) * DesertWorld.Cell})"; }
            if (DesertWorld.DistToCathedral(s.CentreX, s.CentreZ) < 100f) clearOfCathedral = false;
            for (int j = i + 1; j < all.Count; j++)
            {
                var t = all[j];
                if (s.fx0 < t.fx1 + 20f && t.fx0 < s.fx1 + 20f && s.fz0 < t.fz1 + 20f && t.fz0 < s.fz1 + 20f) { noOverlap = false; if (whyOverlap.Length == 0) whyOverlap = $" ({s} and {t})"; }
            }
            // the ground under a site is flat at its pad height
            float cxm = (s.fx0 + s.fx1) * 0.5f, czm = (s.fz0 + s.fz1) * 0.5f;
            float e1 = Math.Abs(DesertWorld.Height(cxm, czm) - (s.padY - 0.08f)), e2 = Math.Abs(DesertWorld.Height(s.fx0 + 1f, s.fz1 - 1f) - (s.padY - 0.08f));
            if (e1 > 0.01f || e2 > 0.01f) { padsOk = false; if (whyPad.Length == 0) whyPad = $" ({s}: {e1:F3} m at the middle, {e2:F3} m at a corner)"; }
        }
        Check("WORLD_SITES_INSIDE_THEIR_CELL", inCell, "every footprint keeps 12 m from the edge of its cell" + whyCell);
        Check("WORLD_SITES_DO_NOT_OVERLAP", noOverlap, "no two footprints within 20 m of each other" + whyOverlap);
        Check("WORLD_CLEAR_AROUND_CATHEDRAL", clearOfCathedral, "no site within 100 m of the cathedral");
        Check("WORLD_PADS_ARE_FLAT", padsOk, "the sand under every footprint is at its pad height" + whyPad);
        var welcome = DesertWorld.Welcome();
        var landmark = DesertWorld.Landmark();
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

    // ------------------------------------------------------------------ the regions
    static void Regions()
    {
        DesertWorld.Reset(20261118);
        Check("REGION_CATHEDRAL_IS_HAMLETS", DesertWorld.RegionAt(23f, 33f) == Region.Hamlets && DesertWorld.RegionAt(60f, 80f) == Region.Hamlets, "the region the player starts in is the Hamlets, whatever the seed");
        bool hamlets = true;
        for (int seed = 1; seed <= 40; seed++) { DesertWorld.Reset(seed * 7919); hamlets &= DesertWorld.RegionAt(23f, 33f) == Region.Hamlets; }
        Check("REGION_CATHEDRAL_IS_HAMLETS_EVERY_SEED", hamlets, "forty seeds, forty times the Hamlets at the steps");
        DesertWorld.Reset(20261118);
        var seen = new Dictionary<Region, int>();
        int samples = 0; bool blendOk = true, tintOk = true, ampOk = true;
        for (float x = -7000f; x <= 7000f; x += 97f)
            for (float z = -7000f; z <= 7000f; z += 101f)
            {
                var r = DesertWorld.RegionAt(x, z);
                seen[r] = (seen.TryGetValue(r, out int c) ? c : 0) + 1;
                samples++;
                DesertWorld.RegionBlend(x, z, out var a, out var b, out float wa);
                if (wa < 0.5f - 1e-4f || wa > 1f + 1e-4f || a != r) blendOk = false;
                DesertWorld.TintAt(x, z, out float tr, out float tg, out float tb, out float td);
                if (tr < 0.6f || tr > 1.4f || tg < 0.6f || tg > 1.4f || tb < 0.5f || tb > 1.4f || td < 0f || td > 1f) tintOk = false;
                float amp = DesertWorld.RegionAmp(x, z);
                if (amp < 0.2f || amp > 1.6f) ampOk = false;
            }
        var parts = new List<string>();
        foreach (var kv in seen) parts.Add($"{kv.Key} {kv.Value * 100 / samples} %");
        Check("REGION_ALL_SIX_EXIST", seen.Count == 6, string.Join(", ", parts));
        Check("REGION_BLEND_WEIGHTS", blendOk, "the first region counts between one half and all, and is the one RegionAt names");
        Check("REGION_TINT_IN_RANGE", tintOk, "the sand colour multiplier stays between 0.5 and 1.4 and the loss of colour between 0 and 1");
        Check("REGION_DUNE_AMPLITUDE_IN_RANGE", ampOk, "the dune height multiplier stays between 0.2 and 1.6");
        // the places of a region are the region's: the dead town has hotels and theatres, the hamlets have houses
        int hamletHouses = 0, hamletBig = 0, deadBig = 0, deadHouses = 0;
        for (int cx = -40; cx <= 40; cx++)
            for (int cz = -40; cz <= 40; cz++)
            {
                var s = DesertWorld.SiteOf(cx, cz);
                if (s == null || s.forced) continue;
                bool big = s.kind == SiteKind.Albergo || s.kind == SiteKind.Teatro || s.kind == SiteKind.Sanatorio || s.kind == SiteKind.Complesso;
                if (s.region == Region.Hamlets) { if (s.kind == SiteKind.Casa) hamletHouses++; if (big) hamletBig++; }
                if (s.region == Region.DeadTown) { if (s.kind == SiteKind.Casa) deadHouses++; if (big) deadBig++; }
            }
        Check("REGION_SITES_FOLLOW_THEIR_REGION", hamletHouses > hamletBig * 2 && deadBig > deadHouses, $"hamlets: {hamletHouses} houses, {hamletBig} big buildings; dead town: {deadHouses} houses, {deadBig} big buildings");
        // a site names its own region and a palette
        bool names = true;
        for (int cx = -8; cx <= 8; cx++)
            for (int cz = -8; cz <= 8; cz++)
            {
                var s = DesertWorld.SiteOf(cx, cz);
                if (s == null) continue;
                if (s.palette < 0 || s.palette > 63 || s.region != DesertWorld.RegionAt((cx + 0.5f) * DesertWorld.Cell, (cz + 0.5f) * DesertWorld.Cell)) names = false;
            }
        Check("REGION_SITE_FIELDS", names, "every site has a region (that of its cell) and a palette 0..63");
    }

    // ------------------------------------------------------------------ the buttes
    static void Buttes()
    {
        DesertWorld.Reset(1000);
        int total = 0, count = 0, badlands = 0, badlandsCells = 0; float tallest = 0f, shortest = float.MaxValue, nearest = float.MaxValue;
        for (int i = -5; i <= 5; i++)
            for (int j = -5; j <= 5; j++)
            {
                total++;
                float px0, pz0;
                bool has = DesertWorld.ButteOfCell(i, j, out px0, out pz0, out float r, out float h, out _, out _);
                if (DesertWorld.RegionAt(px0, pz0) == Region.Badlands) badlandsCells++;
                if (!has) continue;
                count++; tallest = Math.Max(tallest, h); shortest = Math.Min(shortest, h);
                nearest = Math.Min(nearest, DesertWorld.DistToCathedral(px0, pz0) - DesertWorld.ButteReachRadii * r);
                if (DesertWorld.RegionAt(px0, pz0) == Region.Badlands) badlands++;
            }
        Check("BUTTES_EXIST", count >= 12 && count <= total * 0.5, $"{count} buttes in {total} squares of {DesertWorld.ButteCell:F0} m (a few in each region, never one in every square)");
        Check("BUTTES_HEIGHTS", shortest >= 28f - 0.01f && tallest <= 95.01f && tallest > 60f, $"from {shortest:F0} to {tallest:F0} m");
        Check("BUTTES_MOSTLY_IN_THE_BADLANDS", badlandsCells > 5 && badlands > count * 0.3, $"{badlands} of {count} buttes stand in the badlands ({badlandsCells} squares have their middle there)");
        Check("BUTTES_KEEP_AWAY_FROM_HOME", nearest >= DesertWorld.ButteKeepOut - 0.5f, $"the nearest skirt is {nearest:F0} m from the cathedral");
        // the height itself, not just the table: nothing round the cathedral, the welcome chapel and the landmark tower, and the nearest butte has a flat top and a steep wall
        float near = 0f;
        for (float x = -250f; x <= 350f; x += 11f)
            for (float z = -450f; z <= 300f; z += 11f)
                if (DesertWorld.DistToCathedral(x, z) < 240f || Math.Abs(x - DesertWorld.LandmarkX) + Math.Abs(z - DesertWorld.LandmarkZ) < 100f) near = Math.Max(near, DesertWorld.Buttes(x, z));
        Check("BUTTES_NOT_AROUND_THE_LANDMARKS", near < 1e-4f, $"largest butte height round home and the landmark tower {near:F3} m");
        float bx = 0, bz = 0, br = 0, bh = 0, ba = 0, bs = 0; float bd = float.MaxValue;
        for (int i = -4; i <= 4; i++)
            for (int j = -4; j <= 4; j++)
                if (DesertWorld.ButteOfCell(i, j, out float px, out float pz, out float r, out float h, out float ang, out float asp))
                {
                    float d = DesertWorld.DistToCathedral(px, pz);
                    if (d < bd) { bd = d; bx = px; bz = pz; br = r; bh = h; ba = ang; bs = asp; }
                }
        float top = 0f, steepest = 0f, topSpread = 0f, minTop = float.MaxValue, maxTop = 0f;
        for (float t = 0f; t <= 2.2f; t += 0.01f)
        {
            float cx = bx + (float)Math.Cos(ba) * br * t, cz = bz + (float)Math.Sin(ba) * br * t;
            float h0 = DesertWorld.Buttes(cx, cz), gx = DesertWorld.Buttes(cx + 0.5f, cz) - DesertWorld.Buttes(cx - 0.5f, cz), gz = DesertWorld.Buttes(cx, cz + 0.5f) - DesertWorld.Buttes(cx, cz - 0.5f);
            steepest = Math.Max(steepest, (float)Math.Sqrt(gx * gx + gz * gz));
            if (t < 0.5f) { minTop = Math.Min(minTop, h0); maxTop = Math.Max(maxTop, h0); }
            if (t == 0f) top = h0;
        }
        topSpread = maxTop - minTop;
        Check("BUTTES_FLAT_TOP_STEEP_WALL", top > bh * 0.85f && topSpread < bh * 0.2f && steepest > 1.4f, $"butte of {bh:F0} m at {bd:F0} m from home: top {top:F0} m, spread {topSpread:F1} m over its middle, steepest wall {Math.Atan(steepest) * 57.3f:F0} degrees");
        // no place under a butte: a pad cut into a wall would be a cliff with a floor
        int sites = 0, touching = 0;
        var near2 = new List<Site>();
        DesertWorld.SitesNear(0f, 0f, 2500f, near2);
        foreach (var s in near2) { sites++; if (!s.forced && DesertWorld.ButteTouches(s.fx0, s.fz0, s.fx1, s.fz1, 44f)) touching++; }
        Check("BUTTES_NEVER_UNDER_A_PLACE", sites > 100 && touching == 0, $"{touching} of {sites} places within 2.5 km touch a butte");
    }

    // ------------------------------------------------------------------ the cathedral's plan and the open-air rooms
    static int openVoid, openTall;
    static string openFirst = "";

    /// <summary>An opening of an open-air room (CeilKind.Open) that looks onto the void is cut in a parapet (Opening.ParapetTop): above it there is no wall to hang a head on.</summary>
    static void OpenRoomCensus(Plan p, string what)
    {
        foreach (var o in p.openings)
        {
            if (o.b >= 0 || !p.rooms[o.a].Open) continue;
            openVoid++;
            if (o.top <= Opening.ParapetTop + 0.01f) continue;
            openTall++;
            if (openFirst.Length == 0) openFirst = $"{what}: {p.rooms[o.a].label} {o.style} on {o.line}/{o.center}, head {o.top:F2} m";
        }
    }

    /// <summary>The cathedral (HubPlan) with and without the desert, nothing built and everything built: it validates, and the wide door of the churchyard onto the stairs is a gap in the parapet.
    /// It was three metres high over a parapet of 1.55 m, and its two reveals and its soffit hung in the air: a black beam of 14 m seen from below as a diagonal in the sky.</summary>
    static void Hub()
    {
        int faults = 0, exits = 0, exitStrays = 0;
        string first = "", exitFirst = "";
        foreach (bool desert in new[] { false, true })
            foreach (bool all in new[] { false, true })
            {
                var plan = HubPlan.Build(_ => all, desert);
                var f = HubPlan.Validate(plan);
                if (f.Count > 0) { faults += f.Count; if (first.Length == 0) first = $"desert {desert}, all built {all}: {f[0]}"; }
                OpenRoomCensus(plan, $"hub (desert {desert}, all built {all})");
                if (!desert) continue;
                foreach (var o in plan.openings)
                {
                    if (o.b >= 0 || o.a != HubPlan.Sagrato || o.style != OpenStyle.PortaLarga) continue;
                    exits++;
                    bool onStairs = Math.Abs((o.Lo + o.Hi) * 0.05f - (HubPlan.StairX0 + HubPlan.StairX1) * 0.5f) < 0.06f && Math.Abs((o.Hi - o.Lo) * 0.1f - (HubPlan.StairX1 - HubPlan.StairX0)) < 0.11f;
                    if (!o.Gap || !o.noGlass || o.sill > 0.001f || !onStairs)
                    {
                        exitStrays++;
                        if (exitFirst.Length == 0) exitFirst = $"head {o.top:F2} m, gap {o.Gap}, no glass {o.noGlass}, sill {o.sill:F2}, as wide as the stairs {onStairs}";
                    }
                }
            }
        Check("HUB_PLANS_VALID", faults == 0, faults == 0 ? "the cathedral passes HubPlan.Validate with and without the desert, with nothing built and with everything" : faults + " faults, first: " + first);
        Check("HUB_EXIT_IS_A_GAP_IN_THE_PARAPET", exits == 2 && exitStrays == 0,
              exitStrays == 0 && exits == 2 ? $"the churchyard's door onto the stairs is as wide as the stairs, glassless, level with the floor and a lintel-less gap at the parapet ({Opening.ParapetTop:F2} m): no soffit, no reveals above the wall" : $"{exitStrays} of {exits} exits are wrong, first: {exitFirst}");
        Check("OPEN_ROOMS_OPEN_ONTO_THE_VOID_UNDER_THE_PARAPET", openVoid > 0 && openTall == 0,
              openTall == 0 ? $"{openVoid} openings of open-air rooms onto the void (the cathedral, buildings, towers) and none is higher than the parapet" : $"{openTall} of {openVoid} are higher than the parapet, first: {openFirst}");
    }

    // ------------------------------------------------------------------ the sand by the walls
    static void Yards(int radius)
    {
        DesertWorld.Reset(20261118);
        var yard = new List<DesertScatterGen.YardItem>(); var doors = new List<(float x, float z)>();
        int sites = 0, withYard = 0, items = 0, atDoor = 0, onFootprint = 0, doorsSeen = 0, badDoors = 0, wanted = 0, found = 0, twice = 0, sampled = 0;
        for (int i = -radius; i <= radius; i++)
            for (int j = -radius; j <= radius; j++)
            {
                var s = DesertWorld.SiteOf(i, j);
                if (s == null || s.plan == null || s.IsTower || s.IsCatacomb) continue;
                sites++;
                DesertScatterGen.Yard(s, yard);
                DesertScatterGen.Doors(s, doors);
                if (yard.Count > 0) withYard++;
                items += yard.Count;
                // a door sits on the wall of its room: for a wall along x the z is the room's edge and the x lies between its sides (and the other way round)
                foreach (var o in s.plan.openings)
                {
                    if (!(o.b < 0 && o.sill < 0.01f && !o.oneSided)) continue;
                    doorsSeen++;
                    var r = s.plan.rooms[o.a];
                    bool onEdge = o.alongX ? (Math.Abs(o.line - r.z0) <= 2 || Math.Abs(o.line - r.z1) <= 2) && o.center >= r.x0 && o.center <= r.x1
                                           : (Math.Abs(o.line - r.x0) <= 2 || Math.Abs(o.line - r.x1) <= 2) && o.center >= r.z0 && o.center <= r.z1;
                    if (!onEdge) badDoors++;
                }
                foreach (var y in yard)
                {
                    foreach (var dr in doors) if ((y.x - dr.x) * (y.x - dr.x) + (y.z - dr.z) * (y.z - dr.z) < 4f * 4f) atDoor++;
                    if (y.x > s.fx0 && y.x < s.fx1 && y.z > s.fz0 && y.z < s.fz1) onFootprint++;
                }
                // each prop of a yard comes out of the square that holds it, and out of no other
                if (sampled < 40 && (i + j) % 4 == 0 && yard.Count > 0)
                {
                    sampled++;
                    var cache = new Dictionary<(int, int), List<PropItem>>();
                    List<PropItem> Squares(int cx, int cz)
                    {
                        if (!cache.TryGetValue((cx, cz), out var l)) { l = new List<PropItem>(); DesertScatterGen.Build(cx, cz, l); cache[(cx, cz)] = l; }
                        return l;
                    }
                    foreach (var y in yard)
                    {
                        wanted++;
                        int hits = 0, mine = 0;
                        int hx = (int)Math.Floor(y.x / DesertScatterGen.Chunk), hz = (int)Math.Floor(y.z / DesertScatterGen.Chunk);
                        for (int a = -1; a <= 1; a++)
                            for (int b = -1; b <= 1; b++)
                                foreach (var it in Squares(hx + a, hz + b))
                                    if (it.kind == (byte)y.kind && Math.Abs(it.x - y.x) < 1e-3f && Math.Abs(it.z - y.z) < 1e-3f) { hits++; if (a == 0 && b == 0) mine++; }
                        if (hits > 1) twice++;
                        if (mine == 1) found++;
                    }
                }
            }
        Check("YARDS_EXIST", sites > 100 && withYard >= 0.9 * sites && items >= 2.5 * sites, $"{withYard} of {sites} buildings have a yard, {items} props ({(sites > 0 ? items / (float)sites : 0f):F1} each)");
        Check("YARDS_KEEP_THE_DOORS_CLEAR", doorsSeen > 100 && atDoor == 0, $"{atDoor} yard props within 4 m of one of {doorsSeen} doors");
        Check("YARDS_DOORS_SIT_ON_THEIR_ROOM_WALL", badDoors == 0, $"{badDoors} of {doorsSeen} doors are not on the wall of their room (the yards read the wrong place)");
        Check("YARDS_STAY_OFF_THE_BUILDINGS", onFootprint == 0, $"{onFootprint} yard props inside a footprint");
        Check("YARDS_COME_OUT_ONCE", wanted > 80 && twice == 0 && found >= 0.75 * wanted, $"{found} of {wanted} yard props come out of their square, {twice} out of two");
    }

    // ------------------------------------------------------------------ what lies on the sand
    static void Scatter(int radius)
    {
        DesertWorld.Reset(20261118);
        var items = new List<PropItem>(); var again = new List<PropItem>();
        int chunks = 0, inFootprint = 0, outOfChunk = 0, same = 0, tooMany = 0, badScale = 0, onCathedral = 0, total = 0;
        var byRegion = new Dictionary<Region, int[]>();
        var squares = new Dictionary<Region, int>();
        var sites = new List<Site>();
        int maxPer = 0;
        for (int cx = -radius; cx <= radius; cx++)
            for (int cz = -radius; cz <= radius; cz++)
            {
                DesertScatterGen.Build(cx, cz, items);
                DesertScatterGen.Build(cx, cz, again);
                chunks++;
                bool eq = items.Count == again.Count;
                for (int i = 0; eq && i < items.Count; i++) eq = items[i].kind == again[i].kind && items[i].x == again[i].x && items[i].z == again[i].z && items[i].y == again[i].y;
                if (eq) same++;
                maxPer = Math.Max(maxPer, items.Count);
                if (items.Count > 400) tooMany++;
                total += items.Count;
                float ox = cx * DesertScatterGen.Chunk, oz = cz * DesertScatterGen.Chunk;
                foreach (var it in items)
                {
                    float lk = DesertScatterGen.Leak;
                    if (it.x < ox - lk || it.x > ox + DesertScatterGen.Chunk + lk || it.z < oz - lk || it.z > oz + DesertScatterGen.Chunk + lk) outOfChunk++;
                    if (it.scale < 0.4f || it.scale > 2.6f) badScale++;
                    if (DesertWorld.DistToCathedral(it.x, it.z) < 14f) onCathedral++;
                    DesertWorld.SitesNear(it.x, it.z, 2f, sites);
                    foreach (var s in sites) if (it.x > s.fx0 && it.x < s.fx1 && it.z > s.fz0 && it.z < s.fz1) inFootprint++;
                }
            }
        Check("SCATTER_DETERMINISTIC", same == chunks, $"{same} of {chunks} squares give the same props twice");
        Check("SCATTER_NOT_ON_SITES", inFootprint == 0, $"{inFootprint} props inside a footprint, {total} props in {chunks} squares");
        Check("SCATTER_NOT_ON_THE_CATHEDRAL", onCathedral == 0, $"{onCathedral} props within 14 m of the cathedral");
        Check("SCATTER_STAYS_NEAR_ITS_SQUARE", outOfChunk == 0, $"{outOfChunk} props more than {DesertScatterGen.Leak:F0} m outside their square");
        Check("SCATTER_COUNT_IN_RANGE", tooMany == 0 && badScale == 0, $"{maxPer} props at most in a square, {badScale} out of scale");
        // per square, by the region of its middle: each region has its own look (a lattice over a few thousand metres, so that every region shows up)
        int span = radius > 14 ? 110 : 70;
        for (int cx = -span; cx <= span; cx += 7)
            for (int cz = -span; cz <= span; cz += 7)
            {
                float x = cx * DesertScatterGen.Chunk + 32f, z = cz * DesertScatterGen.Chunk + 32f;
                if (DesertBiomes.At(x, z, DesertWorld.Seed).Desert < 0.98f) continue;
                DesertScatterGen.Build(cx, cz, items);
                var reg = DesertWorld.RegionAt(x, z);
                if (!byRegion.TryGetValue(reg, out var counts)) byRegion[reg] = counts = new int[(int)Prop.Count];
                squares[reg] = (squares.TryGetValue(reg, out int sq) ? sq : 0) + 1;
                foreach (var it in items) counts[it.kind]++;
            }
        float Per(Region r, params Prop[] ps)
        {
            if (!byRegion.TryGetValue(r, out var c) || !squares.TryGetValue(r, out int n) || n == 0) return 0f;
            int t = 0; foreach (var p in ps) t += c[(int)p];
            return (float)t / n;
        }
        var reds = new[] { Prop.RockRed, Prop.BoulderRed };
        float badReds = Per(Region.Badlands, reds), saltReds = Per(Region.Salt, reds);
        float saltCacti = Per(Region.Salt, Prop.Cactus, Prop.Barrel), dunesCacti = Per(Region.Dunes, Prop.Cactus, Prop.Barrel);
        float holyStone = Per(Region.Holy, Prop.Column, Prop.Stele), dunesStone = Per(Region.Dunes, Prop.Column, Prop.Stele);
        float hamletBush = Per(Region.Hamlets, Prop.Bush), saltBush = Per(Region.Salt, Prop.Bush);
        bool every = squares.Count == 6;
        Check("SCATTER_FOLLOWS_THE_REGION", every && badReds > 3f * saltReds + 1f && saltCacti < 0.5f * dunesCacti && holyStone > 3f * dunesStone && hamletBush > 3f * saltBush,
              $"per square: red rocks {badReds:F1} in badlands vs {saltReds:F1} in salt; cacti {saltCacti:F1} in salt vs {dunesCacti:F1} in dunes; columns and steles {holyStone:F1} in holy land vs {dunesStone:F1} in dunes; bushes {hamletBush:F1} in hamlets vs {saltBush:F1} in salt ({squares.Count} regions seen)");
        // the solid ones have a size, the others none
        bool solidOk = true;
        for (int k = 0; k < (int)Prop.Count; k++)
        {
            DesertScatterGen.Solid((Prop)k, out float r, out float h);
            var p = (Prop)k;
            bool big = p == Prop.BoulderTan || p == Prop.BoulderDark || p == Prop.BoulderRed || p == Prop.Slab || p == Prop.Cactus || p == Prop.DeadTree || p == Prop.Column || p == Prop.Stele
                       || p == Prop.Tent || p == Prop.FirePit || p == Prop.Crate || p == Prop.Well || p == Prop.Obelisk || p == Prop.Windmill || p == Prop.Wagon
                       || p == Prop.Broadleaf || p == Prop.Fir || p == Prop.FrostFir || p == Prop.GlacierShard;
            if (big != (r > 0f) || (r > 0f && h <= 0f)) solidOk = false;
        }
        Check("SCATTER_SOLID_SHAPES", solidOk, "boulders, slabs, cacti, dead trees, columns, steles, tents, fires, crates, wells, obelisks, windmills and wagons are solid (a capsule with a radius and a height), the rest are not");
        // a camp: a fire with one to three tents round it, doors to the fire, and supplies
        int camps = 0, campsOk = 0, tentsTotal = 0, orphanTents = 0;
        for (int cx = -radius; cx <= radius; cx++)
            for (int cz = -radius; cz <= radius; cz++)
            {
                DesertScatterGen.Build(cx, cz, items);
                var fireList = new List<PropItem>(); var tentList = new List<PropItem>();
                foreach (var it in items) { if (it.kind == (byte)Prop.FirePit) fireList.Add(it); else if (it.kind == (byte)Prop.Tent) tentList.Add(it); }
                camps += fireList.Count; tentsTotal += tentList.Count;
                foreach (var f in fireList)
                {
                    int tents = 0; bool facing = true;
                    foreach (var t in tentList)
                    {
                        float dx = t.x - f.x, dz = t.z - f.z, d = (float)Math.Sqrt(dx * dx + dz * dz);
                        if (d > 6.2f) continue;
                        tents++;
                        // the door looks at the fire: the tent's local -x, turned by its yaw, points towards the fire
                        double yr = t.yaw * Math.PI / 180.0;
                        double fx = -Math.Cos(yr), fz = Math.Sin(yr);       // local -x after Euler(0, yaw, 0): (-cos yaw, 0, +sin yaw)
                        if (fx * (-dx / d) + fz * (-dz / d) < 0.95) facing = false;
                    }
                    if (tents >= 1 && tents <= 3 && facing) campsOk++;
                }
                foreach (var t in tentList) { bool near = false; foreach (var f in fireList) { float dx = t.x - f.x, dz = t.z - f.z; if (dx * dx + dz * dz < 6.3f * 6.3f) near = true; } if (!near) orphanTents++; }
            }
        Check("SCATTER_CAMPS", camps >= 3 && campsOk >= camps - 2 && orphanTents <= camps, $"{camps} fires in {radius * 2 + 1}x{radius * 2 + 1} squares, {campsOk} with one to three tents with their doors to the fire, {tentsTotal} tents ({orphanTents} cut off from their fire by the edge of a square)");
    }

    // ------------------------------------------------------------------ the air
    static void Weather()
    {
        bool det = true, range = true, firstClear = true, unit = true, smooth = true;
        float maxStep = 0f;
        for (int seed = 1; seed <= 40; seed++)
        {
            for (int day = 0; day < 12; day++)
                for (float h = 0f; h < 24f; h += 0.25f)
                {
                    float dl = DesertClock.Evaluate(h).daylight;
                    var a = DesertWeather.Evaluate(seed, day, h, dl);
                    var b = DesertWeather.Evaluate(seed, day, h, dl);
                    if (a.wind != b.wind || a.storm != b.storm || a.dirX != b.dirX) det = false;
                    if (a.wind < 0f || a.wind > 1f || a.gust < 0f || a.gust > 1f || a.storm < 0f || a.storm > 1f || a.fogMul < 1f || a.fogMul > 4f || a.sunMul < 0.4f || a.sunMul > 1f || a.sand < 0f || a.sand > 1f) range = false;
                    if (Math.Abs(a.dirX * a.dirX + a.dirZ * a.dirZ - 1f) > 1e-3f) unit = false;
                    if (day == 0 && h < DesertWeather.FirstClear && a.storm > 0f) firstClear = false;
                }
            // a storm grows and fades smoothly: no jump bigger than a few percent in a quarter of a game hour
            float prev = 0f;
            for (float t = 0f; t < 12f * 24f; t += 0.1f)
            {
                var s = DesertWeather.Evaluate(seed, (int)(t / 24f), t % 24f, 1f);
                if (t > 0f) maxStep = Math.Max(maxStep, Math.Abs(s.storm - prev));
                prev = s.storm;
            }
        }
        smooth = maxStep < 0.45f;
        Check("WEATHER_DETERMINISTIC", det, "the same seed, day and hour give the same weather");
        Check("WEATHER_RANGES", range && unit, "wind, gust, storm and sand stay in 0..1, the fog grows at most four times, the sun loses at most 60 %, the wind direction is a unit vector");
        Check("WEATHER_FIRST_MORNING_CLEAR", firstClear, "no storm before 12:00 of the first day, on forty worlds");
        Check("WEATHER_STORM_IS_SMOOTH", smooth, $"the storm never changes by more than {maxStep:F2} in a tenth of a game hour");
        int days = 0, stormy = 0; float peak = 0f, wettest = 0f;
        for (int day = 0; day < 200; day++)
        {
            if (DesertWeather.Storm(77, day, out float s0, out float len, out float str))
            {
                stormy++;
                if (len < 1.6f || len > 3.21f || str < 0.7f || str > 1.001f || s0 < day * 24f + 7.99f || s0 > day * 24f + 22.01f) peak = -99f;
            }
            days++;
        }
        for (float t = 0f; t < 24f * 200f; t += 0.2f)
        {
            var s = DesertWeather.Evaluate(77, (int)(t / 24f), t % 24f, 1f);
            peak = Math.Max(peak, s.storm);
            wettest = Math.Max(wettest, s.fogMul);
        }
        float rate = (float)stormy / days;
        Check("WEATHER_STORMS_HAPPEN", rate > 0.4f && rate < 0.7f && peak >= 0.7f, $"{stormy} of {days} days have a storm ({rate * 100f:F0} %), the strongest reaches {peak:F2} and thickens the day's fog {wettest:F1} times");
        var noon = DesertWeather.Evaluate(77, 0, 12f, 1f);
        float tStorm = 0f; bool found = false;
        for (int day = 1; day < 40 && !found; day++)
            if (DesertWeather.Storm(77, day, out float s0, out float len, out float str)) { tStorm = s0 + len * 0.5f; found = true; }
        var mid = DesertWeather.Evaluate(77, (int)(tStorm / 24f), tStorm % 24f, 1f);
        var dark = DesertWeather.Evaluate(77, (int)(tStorm / 24f), tStorm % 24f, 0f);
        Check("WEATHER_STORM_BLOWS_AND_DARKENS", found && mid.storm > 0.69f && mid.wind > 0.9f && mid.fogMul > 2.3f && mid.sunMul < 0.7f && dark.fogMul < mid.fogMul && noon.storm == 0f,
              $"in the middle of a storm: wind {mid.wind:F2}, fog x{mid.fogMul:F1} by day and x{dark.fogMul:F1} by night, sun x{mid.sunMul:F2}");
    }

    // ------------------------------------------------------------------ the picture's grading
    static void Grading()
    {
        bool range = true;
        float maxStep = 0f, minSat = 999f, maxSat = -999f;
        for (int seed = 1; seed <= 12; seed++)
            for (int day = 0; day < 4; day++)
            {
                Grade prev = default; bool havePrev = false;
                for (float h = 0f; h < 24f; h += 0.05f)
                {
                    var s = DesertClock.Evaluate(h);
                    var w = DesertWeather.Evaluate(seed, day, h, s.daylight);
                    var g = DesertGrade.Outdoors(s, w);
                    minSat = Math.Min(minSat, g.saturation); maxSat = Math.Max(maxSat, g.saturation);
                    if (g.saturation < -40f || g.saturation > 40f || g.contrast < 0f || g.contrast > 40f || g.bloom < 0.2f || g.bloom > 1.6f || g.bloomThreshold < 0.8f || g.bloomThreshold > 3.2f || g.vignette < 0.1f || g.vignette > 0.5f || g.grain < 0.05f || g.grain > 0.35f
                        || g.balance < -50f || g.balance > 50f) range = false;
                    foreach (var c in new[] { g.filter, g.shadows, g.highlights })
                        if (c.r < 0f || c.g < 0f || c.b < 0f || c.r > 1.5f || c.g > 1.5f || c.b > 1.5f) range = false;
                    // the clock alone (no weather) moves the grade smoothly: a storm has its own pace (the weather's, checked above)
                    var calm = DesertGrade.Outdoors(s, DesertWeather.WithStorm(w, 0f, s.daylight));
                    if (havePrev) maxStep = Math.Max(maxStep, Math.Max(Math.Abs(calm.saturation - prev.saturation), Math.Max(Math.Abs(calm.bloom - prev.bloom) * 20f, Math.Abs(calm.bloomThreshold - prev.bloomThreshold) * 3f)));
                    prev = calm; havePrev = true;
                }
            }
        bool smooth = maxStep < 3.0f;
        Check("GRADE_RANGES", range, $"saturation {minSat:F0}..{maxSat:F0}; contrast, bloom, vignette, grain, balance and the three tints stay in sane ranges for 12 worlds x 4 days x 24 hours");
        Check("GRADE_IS_SMOOTH", smooth, $"between two samples 3 game minutes apart the saturation changes by at most {maxStep:F1} points (and the bloom by a twentieth of that scale)");
        var none = DesertWeather.WithStorm(DesertWeather.Evaluate(1, 0, 12f, 1f), 0f, 1f);
        var noon = DesertGrade.Outdoors(DesertClock.Evaluate(12f), none);
        var gold = DesertGrade.Outdoors(DesertClock.Evaluate(17.9f), DesertWeather.WithStorm(DesertWeather.Evaluate(1, 0, 17.9f, DesertClock.Evaluate(17.9f).daylight), 0f, DesertClock.Evaluate(17.9f).daylight));
        var night = DesertGrade.Outdoors(DesertClock.Evaluate(0.5f), DesertWeather.WithStorm(DesertWeather.Evaluate(1, 0, 0.5f, 0f), 0f, 0f));
        var sand = DesertGrade.Outdoors(DesertClock.Evaluate(12f), DesertWeather.WithStorm(none, 1f, 1f));
        Check("GRADE_GOLDEN_HOUR_IS_WARM", gold.highlights.r - gold.highlights.b > noon.highlights.r - noon.highlights.b && gold.saturation > noon.saturation && gold.bloom > noon.bloom,
              $"at 17:54 the highlights are redder ({gold.highlights.r - gold.highlights.b:F2} vs {noon.highlights.r - noon.highlights.b:F2} at noon), the colours stronger ({gold.saturation:F0} vs {noon.saturation:F0}) and the bloom bigger ({gold.bloom:F2} vs {noon.bloom:F2})");
        Check("GRADE_NIGHT_IS_COOL", night.highlights.b > night.highlights.r && night.shadows.b > night.shadows.r && night.saturation < noon.saturation && night.grain > noon.grain,
              $"at midnight the lights and shadows lean blue, the colour is lower ({night.saturation:F0} vs {noon.saturation:F0} at noon) and the grain higher ({night.grain:F2} vs {noon.grain:F2})");
        Check("GRADE_STORM_IS_DUSTY", sand.saturation < noon.saturation - 15f && sand.filter.r > sand.filter.b + 0.1f && sand.vignette > noon.vignette,
              $"in a full sandstorm the colour drops ({sand.saturation:F0} vs {noon.saturation:F0}), the picture is filtered orange and the corners darken more");
        Check("GRADE_BLOOM_IS_HIGH_IN_THE_SUN", noon.bloomThreshold >= 2.2f && night.bloomThreshold <= 1.3f && gold.bloomThreshold < noon.bloomThreshold && gold.bloomThreshold > night.bloomThreshold,
              $"the bloom picks up only the very bright parts by day (threshold {noon.bloomThreshold:F1} at noon, {gold.bloomThreshold:F1} at golden hour, {night.bloomThreshold:F1} at night): the sand is above 1 almost everywhere in the sun, and with the rooms' threshold of 1 the whole picture glowed milky");
        Check("GRADE_DAY_PUSHES_COLOUR", noon.saturation > 5f && noon.contrast > 10f, $"a clear noon is graded with saturation {noon.saturation:F0} and contrast {noon.contrast:F0}: the rooms' grey look (-26) does not reach the sand");
        // URP's split toning is a 'soft light' blend: a warm tint on the highlights lifts the red of everything bright, the sky included, and a blue sky turns grey-lilac (it did, with 0.98 / 0.80 / 0.58)
        Check("GRADE_DAY_HIGHLIGHTS_KEEP_THE_SKY_BLUE", noon.highlights.r - noon.highlights.b < 0.20f && noon.highlights.r - noon.highlights.b >= 0f,
              $"by day the highlights are only a touch warm (red - blue = {noon.highlights.r - noon.highlights.b:F2}, golden hour {gold.highlights.r - gold.highlights.b:F2}): a strong tint made the blue sky grey-lilac");
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
