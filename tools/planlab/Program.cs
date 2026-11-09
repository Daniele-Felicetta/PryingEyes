using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PryingEyes.Proc;

// Laboratorio del pianificatore: genera migliaia di piani senza aprire Unity, li controlla e disegna le mappe.
//   dotnet run -c Release -- stats 2000
//   dotnet run -c Release -- map <seme> <piano> <file.ppm>
//   dotnet run -c Release -- sheet <seme> <quanti> <file.ppm>
static class Program
{
    static int Main(string[] a)
    {
        string cmd = a.Length > 0 ? a[0] : "stats";
        if (cmd == "stats") return Stats(a.Length > 1 ? int.Parse(a[1]) : 1000);
        if (cmd == "map") { Map(int.Parse(a[1]), int.Parse(a[2]), a[3]); return 0; }
        if (cmd == "sheet") { Sheet(int.Parse(a[1]), int.Parse(a[2]), a[3]); return 0; }
        return 1;
    }

    static int Stats(int n)
    {
        int fail = 0; var attempts = new int[81]; var sw = Stopwatch.StartNew();
        double rooms = 0, area = 0; int maxRooms = 0, minRooms = 999;
        var kinds = new Dictionary<RoomKind, int>(); int churchStart = 0, openings = 0;
        double maxW = 0, maxD = 0, pathSum = 0;
        var reasons = new Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            int seed = 1000 + i * 17, floor = 1 + i % 12;
            var p = PlanGen.Generate(seed, floor);
            if (p == null) { fail++; string r = PlanGen.LastFailure; reasons[r] = reasons.TryGetValue(r, out int c) ? c + 1 : 1; continue; }
            attempts[PlanGen.LastAttempts]++;
            rooms += p.rooms.Count; maxRooms = Math.Max(maxRooms, p.rooms.Count); minRooms = Math.Min(minRooms, p.rooms.Count);
            foreach (var r in p.rooms) { kinds[r.kind] = kinds.TryGetValue(r.kind, out int c) ? c + 1 : 1; area += r.W * r.D / 100.0; }
            if (p.startInChurch) churchStart++;
            openings += p.openings.Count;
            maxW = Math.Max(maxW, (p.maxX - p.minX) / 10.0); maxD = Math.Max(maxD, (p.maxZ - p.minZ) / 10.0);
            pathSum += p.WalkDist(p.start)[p.exit];
        }
        sw.Stop();
        Console.WriteLine($"{n} piani in {sw.ElapsedMilliseconds} ms ({sw.ElapsedMilliseconds / (double)n:F2} ms/piano), falliti {fail}");
        Console.Write("tentativi: ");
        for (int i = 1; i <= 80; i++) if (attempts[i] > 0) Console.Write($"{i}:{attempts[i]} ");
        Console.WriteLine();
        int ok = Math.Max(1, n - fail);
        Console.WriteLine($"stanze {rooms / ok:F1} (min {minRooms}, max {maxRooms}), area {area / ok:F0} m2, aperture {openings / (double)ok:F1}, partenza in chiesa {churchStart * 100 / ok}%");
        Console.WriteLine($"ingombro massimo {maxW:F0} x {maxD:F0} m, cammino medio arrivo-uscita {pathSum / ok:F0} m");
        foreach (var kv in kinds) Console.WriteLine($"  {kv.Key}: {kv.Value / (double)ok:F2} per piano");
        foreach (var kv in reasons) Console.WriteLine($"  motivo: {kv.Key} x{kv.Value}");
        return fail == 0 ? 0 : 2;
    }

    static readonly Dictionary<RoomKind, (byte r, byte g, byte b)> Col = new Dictionary<RoomKind, (byte, byte, byte)>
    {
        { RoomKind.Atrio, (200, 170, 110) }, { RoomKind.Corridoio, (150, 120, 90) }, { RoomKind.Camera, (170, 130, 130) },
        { RoomKind.Suite, (190, 120, 150) }, { RoomKind.Ristorante, (210, 150, 70) }, { RoomKind.Cucina, (140, 160, 170) },
        { RoomKind.Servizio, (110, 110, 110) }, { RoomKind.SalaBallo, (230, 190, 90) },
        { RoomKind.Nartece, (120, 150, 190) }, { RoomKind.Navata, (100, 130, 200) }, { RoomKind.Navatella, (90, 160, 190) },
        { RoomKind.Cappella, (150, 110, 200) }, { RoomKind.Presbiterio, (70, 100, 190) }, { RoomKind.Sacrestia, (130, 100, 170) },
        { RoomKind.Cripta, (80, 70, 110) }, { RoomKind.Campanile, (100, 140, 140) }, { RoomKind.Ascensore, (80, 200, 80) },
    };

    static byte[] Render(Plan p, int px, out int W, out int H)
    {
        float sc = px / 10f;   // pixel per decimetro
        int pad = 20;
        W = (int)((p.maxX - p.minX) * sc) + pad * 2; H = (int)((p.maxZ - p.minZ) * sc) + pad * 2;
        var img = new byte[W * H * 3];
        for (int i = 0; i < img.Length; i += 3) { img[i] = 18; img[i + 1] = 18; img[i + 2] = 22; }
        int minX = p.minX, minZ = p.minZ, hh = H, ww = W;
        void Px(int x, int y, (byte r, byte g, byte b) c)
        {
            if (x < 0 || y < 0 || x >= ww || y >= hh) return;
            int k = (y * ww + x) * 3; img[k] = c.r; img[k + 1] = c.g; img[k + 2] = c.b;
        }
        int X(int dm) => pad + (int)((dm - minX) * sc);
        int Y(int dm) => hh - 1 - (pad + (int)((dm - minZ) * sc));
        void Fill(int x0, int z0, int x1, int z1, (byte r, byte g, byte b) c)
        {
            int xa = X(x0), xb = X(x1), ya = Y(z1), yb = Y(z0);
            for (int y = ya; y < yb; y++) for (int x = xa; x < xb; x++) Px(x, y, c);
        }
        foreach (var r in p.rooms)
        {
            Fill(r.x0, r.z0, r.x1, r.z1, Col[r.kind]);
            var wall = ((byte)15, (byte)15, (byte)15);
            Fill(r.x0, r.z0, r.x1, r.z0 + 1, wall); Fill(r.x0, r.z1 - 1, r.x1, r.z1, wall);
            Fill(r.x0, r.z0, r.x0 + 1, r.z1, wall); Fill(r.x1 - 1, r.z0, r.x1, r.z1, wall);
        }
        foreach (var o in p.openings)
        {
            (byte r, byte g, byte b) cc = o.b < 0 ? ((byte)90, (byte)170, (byte)255) : o.style == OpenStyle.Arcata ? ((byte)255, (byte)240, (byte)180) : o.style == OpenStyle.Ascensore ? ((byte)255, (byte)255, (byte)255) : ((byte)255, (byte)220, (byte)60);
            if (o.oneSided) cc = ((byte)40, (byte)110, (byte)170);
            int t = 2;
            if (o.alongX) Fill(o.Lo, o.line - t, o.Hi, o.line + t, cc); else Fill(o.line - t, o.Lo, o.line + t, o.Hi, cc);
        }
        var sr = p.rooms[p.start]; var er = p.rooms[p.exit];
        Fill(sr.x0 + 6, sr.z0 + 6, sr.x1 - 6, sr.z1 - 6, ((byte)40, (byte)255, (byte)40));
        Fill(er.x0 + 6, er.z0 + 6, er.x1 - 6, er.z1 - 6, ((byte)255, (byte)40, (byte)40));
        return img;
    }

    static void WritePpm(string path, byte[] img, int w, int h)
    {
        using (var f = File.Create(path))
        {
            var head = System.Text.Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
            f.Write(head, 0, head.Length); f.Write(img, 0, img.Length);
        }
    }

    static void Map(int seed, int floor, string path)
    {
        var p = PlanGen.Generate(seed, floor);
        if (p == null) { Console.WriteLine("nessun piano: " + PlanGen.LastFailure); return; }
        var img = Render(p, 6, out int w, out int h);
        WritePpm(path, img, w, h);
        Console.WriteLine($"seme {seed} piano {floor}: {p.rooms.Count} stanze, {p.openings.Count} aperture, tentativi {PlanGen.LastAttempts}, {(p.maxX - p.minX) / 10.0:F0}x{(p.maxZ - p.minZ) / 10.0:F0} m, partenza {(p.startInChurch ? "chiesa" : "albergo")}, cammino {p.WalkDist(p.start)[p.exit]:F0} m");
    }

    // foglio di mappe: n piani affiancati in un solo file
    static void Sheet(int seed0, int n, string path)
    {
        var imgs = new List<(byte[] d, int w, int h)>();
        for (int i = 0; i < n; i++)
        {
            var p = PlanGen.Generate(seed0 + i * 17, 1 + i % 6);
            if (p == null) continue;
            var d = Render(p, 3, out int w, out int h);
            imgs.Add((d, w, h));
        }
        int cols = 3, rows = (imgs.Count + cols - 1) / cols;
        int cw = 0, ch = 0; foreach (var im in imgs) { cw = Math.Max(cw, im.w); ch = Math.Max(ch, im.h); }
        int W = cols * cw, H = rows * ch;
        var big = new byte[W * H * 3];
        for (int i = 0; i < imgs.Count; i++)
        {
            int ox = (i % cols) * cw, oy = (i / cols) * ch;
            var (d, w, h) = imgs[i];
            for (int y = 0; y < h; y++) Array.Copy(d, y * w * 3, big, ((oy + y) * W + ox) * 3, w * 3);
        }
        WritePpm(path, big, W, H);
        Console.WriteLine($"foglio {imgs.Count} piani {W}x{H}");
    }
}
