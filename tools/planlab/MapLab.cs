using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using PryingEyes.Proc;
using PryingEyes.Proc.MapArt;

// Anteprima della mappa senza Unity: dipinge un piano con il pittore del gioco (Scripts/Proc/Map) e lo rasterizza in un PNG.
//   dotnet run -c Release -- waves mapart <seme del piano> <piano> <file.png> [all|walk|start] [larghezza] [mini]
// Il testo non si disegna (sono le etichette di Unity): al suo posto un segno per ogni etichetta. Gli arredi sono finti (in gioco sono i veri).
static class MapLab
{
    public static int Run(string[] a)
    {
        int seed = int.Parse(a[1]), floor = int.Parse(a[2]);
        string path = a[3];
        string mode = a.Length > 4 ? a[4] : "walk";
        int width = a.Length > 5 ? int.Parse(a[5]) : 1500;
        bool mini = Array.IndexOf(a, "mini") >= 0;
        var plan = PlanGen.Generate(seed, floor);
        if (plan == null) { Console.WriteLine("piano non generato: " + PlanGen.LastFailure); return 1; }
        var scene = BuildScene(plan, mode, seed);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var painter = new MapPainter(scene);
        var all = new List<Art>();
        for (int i = 0; i < plan.rooms.Count; i++) all.Add(painter.Room(i));
        all.Add(painter.Doors()); all.Add(painter.Pois()); all.Add(painter.Blood());
        long verts = 0; foreach (var art in all) verts += art.Vertices;
        double paintMs = sw.Elapsed.TotalMilliseconds;
        var b = painter.PlanBounds(6f);
        R2 view = b;
        if (mini)
        {
            var c = plan.rooms[plan.start].Contains(0, 0) ? new V2(0, 0) : RoomCenter(plan, scene.seen, plan.start);
            view = new R2(c.x - 17f, c.y - 17f, c.x + 17f, c.y + 17f);
        }
        float vw = view.W, vh = view.H;
        // il foglio ha la proporzione del piano, entro il rettangolo richiesto
        int w = width, h = (int)(width * (mini ? 1f : Math.Min(1f, Math.Max(0.55f, vh / vw))));
        var tex = Textures();
        var img = new Raster(w, h, 2);
        img.PaperBackdrop(tex.paper, view);
        float ppm = img.ppm(view);
        foreach (var art in all) img.Mesh(art.wash, view, tex.wash, true);
        foreach (var art in all) img.Mesh(art.ink, view, tex.line, false);
        foreach (var art in all) img.Mesh(art.stain, view, tex.stain, false);
        foreach (var art in all)
            foreach (var l in art.labels) img.LabelMark(l, view);
        img.Vignette();
        File.WriteAllBytes(path, img.Png());
        Console.WriteLine($"mapart seme {seed} piano {floor} ({plan.theme}, {plan.rooms.Count} stanze, {scene.props.Count} arredi finti): {verts} vertici, dipinta in {paintMs:F0} ms, {ppm:F1} px/m -> {path}");
        return 0;
    }

    static (PixelMap line, PixelMap wash, PixelMap stain, PixelMap paper) Textures()
    {
        return (MapTextures.LineTex(), MapTextures.WashTex(), MapTextures.StainAtlas(), MapTextures.PaperTex());
    }

    static V2 RoomCenter(Plan p, RoomSeen[] seen, int id) => new V2(p.rooms[id].CenterX, p.rooms[id].CenterZ);

    // ------------------------------------------------------------------ la scena di prova
    public static MapScene BuildScene(Plan plan, string mode, int seed)
    {
        var s = new MapScene(plan) { seed = seed };
        s.paidDoors = PlanGen.SurvivalLayout && plan.grid;
        int start = plan.grid ? FindStartArea(plan) : plan.start;
        var depth = plan.Hops(start);
        for (int i = 0; i < plan.rooms.Count; i++)
        {
            var r = plan.rooms[i];
            if (r.IsLift) continue;
            if (mode == "all") s.seen[i] = r.type == RoomType.Segreta ? RoomSeen.Hidden : RoomSeen.Seen;
            else if (mode == "start") s.seen[i] = i == start ? RoomSeen.Seen : RoomSeen.Hidden;
            else s.seen[i] = depth[i] <= 1 ? RoomSeen.Seen : depth[i] == 2 ? RoomSeen.Rumor : RoomSeen.Hidden;
        }
        if (mode == "start")
            foreach (var o in plan.openings)
            {
                if (o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore || o.style == OpenStyle.Passaggio) continue;
                if (o.a == start && s.seen[o.b] == RoomSeen.Hidden) s.seen[o.b] = RoomSeen.Rumor;
                if (o.b == start && s.seen[o.a] == RoomSeen.Hidden) s.seen[o.a] = RoomSeen.Rumor;
            }
        // porte a pagamento: aperte le stanze viste da vicino, chiuse le altre
        if (s.paidDoors)
        {
            var access = new RoomAccessState(plan);
            for (int i = 0; i < plan.rooms.Count; i++)
                if (s.seen[i] == RoomSeen.Seen && !plan.rooms[i].IsLift && depth[i] <= (mode == "all" ? 99 : 0)) { }
            var open = new HashSet<int> { access.StartRoom };
            if (mode == "all") foreach (var r in plan.rooms) open.Add(r.id);
            else if (mode == "walk") foreach (var r in plan.rooms) if (depth[r.id] >= 0 && depth[r.id] <= 1) open.Add(r.id);
            access.ApplySnapshot(open);
            for (int i = 0; i < plan.rooms.Count; i++) { s.open[i] = access.IsOpen(i); s.price[i] = access.Price(i); }
            for (int i = 0; i < plan.openings.Count; i++)
            {
                var o = plan.openings[i];
                if (o.b < 0 || o.oneSided) { s.door[i] = DoorState.Open; continue; }
                bool oa = access.IsOpen(o.a), ob = access.IsOpen(o.b);
                s.door[i] = oa && ob ? DoorState.Open : oa != ob ? DoorState.Sealed : DoorState.SealedFar;
                s.doorPrice[i] = oa && !ob ? access.Price(o.b) : !oa && ob ? access.Price(o.a) : 0;
            }
        }
        FakeProps(s);
        FakePois(s, start);
        // sangue nelle stanze viste
        var rng = new Rng(seed);
        foreach (var r in plan.rooms)
        {
            if (s.seen[r.id] != RoomSeen.Seen) continue;
            int n = rng.Next(0, 9);
            for (int k = 0; k < n; k++)
                s.blood.Add(new MapBlood { x = r.x0 * 0.1f + 2f + rng.Float() * (r.W * 0.1f - 4f), z = r.z0 * 0.1f + 2f + rng.Float() * (r.D * 0.1f - 4f), size = rng.Float(), room = r.id });
        }
        return s;
    }

    static int FindStartArea(Plan plan)
    {
        var starts = plan.OfType(RoomType.Partenza);
        return starts.Count > 0 ? starts[0].id : plan.start;
    }

    sealed class Rng
    {
        uint x;
        public Rng(int seed) { x = (uint)seed * 2654435761u + 12345u; }
        public uint Raw() { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return x; }
        public float Float() => (Raw() & 0xFFFFFF) / 16777216f;
        public int Next(int lo, int hi) => lo + (int)(Float() * (hi - lo));
    }

    // arredi finti: il pittore vuole vedere pance e file di panche; in gioco i veri vengono dal piano costruito
    static void FakeProps(MapScene s)
    {
        var plan = s.plan;
        var rng = new Rng(s.seed + 5);
        foreach (var r in plan.rooms)
        {
            if (r.IsLift) continue;
            float x0 = r.x0 * 0.1f + 1.6f, x1 = r.x1 * 0.1f - 1.6f, z0 = r.z0 * 0.1f + 1.6f, z1 = r.z1 * 0.1f - 1.6f;
            if (x1 - x0 < 3f || z1 - z0 < 3f) continue;
            void Add(float x, float z, float w, float d, float yaw, PropClass c) => s.props.Add(new MapProp { x = x, z = z, w = w, d = d, yaw = yaw, cls = c, room = r.id });
            bool church = r.church;
            if (church || r.kind == RoomKind.Platea)
            {
                // file di panche con la navata libera al centro
                float cx = (x0 + x1) * 0.5f;
                for (float z = z0 + 2f; z < z1 - 4f; z += 2.2f)
                {
                    Add(cx - 5.5f, z, 2.6f, 0.9f, 0f, PropClass.Pew); Add(cx - 2.7f, z, 2.6f, 0.9f, 0f, PropClass.Pew);
                    Add(cx + 2.7f, z, 2.6f, 0.9f, 0f, PropClass.Pew); Add(cx + 5.5f, z, 2.6f, 0.9f, 0f, PropClass.Pew);
                }
            }
            else if (r.kind == RoomKind.Camera || r.kind == RoomKind.Suite || r.kind == RoomKind.Degenza || r.kind == RoomKind.Corsia)
            {
                for (float x = x0 + 1f; x < x1 - 1f; x += 3.2f) { Add(x, z0 + 1.2f, 1.75f, 2.15f, 1.5708f, PropClass.Bed); Add(x, z1 - 1.2f, 1.75f, 2.15f, 1.5708f, PropClass.Bed); }
            }
            else if (r.kind == RoomKind.Ristorante || r.kind == RoomKind.Refettorio || r.kind == RoomKind.SalaBallo)
            {
                for (float x = x0 + 2f; x < x1 - 2f; x += 5.5f)
                    for (float z = z0 + 2f; z < z1 - 2f; z += 5.5f)
                    {
                        Add(x, z, 0.95f, 0.95f, 0f, PropClass.Table);
                        Add(x - 1f, z, 0.5f, 0.5f, 0f, PropClass.Chair); Add(x + 1f, z, 0.5f, 0.5f, 0f, PropClass.Chair);
                    }
            }
            else if (r.kind == RoomKind.Archivio || r.kind == RoomKind.Deposito)
            {
                for (float z = z0 + 1f; z < z1 - 1f; z += 3.6f) for (float x = x0 + 1f; x < x1 - 1.5f; x += 1.4f) Add(x, z, 1.2f, 0.45f, 0f, PropClass.Shelf);
            }
            // colonne in ogni arena e nelle stanze grandi
            if (r.W * r.D > 3000 || r.type == RoomType.Candela)
                for (float z = z0 + 4f; z < z1 - 3f; z += 9f) for (float x = x0 + 4f; x < x1 - 3f; x += 9f) Add(x + (rng.Float() - 0.5f) * 1.5f, z, 0.9f, 0.9f, 0f, PropClass.Column);
            // qualche pezzo sparso
            int extra = (int)(r.W * r.D * 0.00028f);
            for (int i = 0; i < extra; i++)
            {
                var cls = (PropClass)rng.Next(0, 14);
                if (cls == PropClass.Column || cls == PropClass.Pew) continue;
                Add(x0 + rng.Float() * (x1 - x0), z0 + rng.Float() * (z1 - z0), 0.8f + rng.Float() * 1.6f, 0.5f + rng.Float() * 0.9f, rng.Float() * 3.14f, cls);
            }
        }
    }

    static void FakePois(MapScene s, int start)
    {
        var plan = s.plan;
        var rng = new Rng(s.seed + 9);
        foreach (var r in plan.rooms)
        {
            if (r.IsLift || s.seen[r.id] != RoomSeen.Seen) continue;
            void Add(PoiKind k, float fx, float fz, string label = null, int price = 0, byte state = 0, byte tint = 0) =>
                s.pois.Add(new MapPoi { kind = k, x = r.x0 * 0.1f + r.W * 0.1f * fx, z = r.z0 * 0.1f + r.D * 0.1f * fz, label = label, price = price, state = state, room = r.id, tint = tint });
            if (r.type == RoomType.Partenza) Add(PoiKind.Weapon, 0.2f, 0.85f, "M1911", 500);
            if (r.type == RoomType.Negozio) { Add(PoiKind.Weapon, 0.25f, 0.9f, "RIVOLTELLA", 750); Add(PoiKind.Weapon, 0.75f, 0.9f, "FUCILE A POMPA", 1200); Add(PoiKind.Scrigno, 0.5f, 0.5f, "Scrigno", 950); }
            if (r.type == RoomType.Biblioteca) Add(PoiKind.Biblioteca, 0.5f, 0.5f, "Biblioteca");
            if (r.type == RoomType.Candela) { Add(PoiKind.Altare, 0.3f, 0.3f, "Altare", 3000); Add(PoiKind.Drop, 0.7f, 0.6f, null, 0, 0, (byte)rng.Next(0, 4)); Add(PoiKind.Barrel, 0.8f, 0.2f); }
            if (r.type == RoomType.Guardia && rng.Float() < 0.5f) Add(PoiKind.Quadro, 0.9f, 0.5f, "Quadro", 0, (byte)(rng.Float() < 0.5f ? 1 : 0));
        }
        // gli ascensori
        foreach (var o in plan.openings)
        {
            if (o.style != OpenStyle.Ascensore) continue;
            var lift = plan.rooms[o.a].IsLift ? plan.rooms[o.a] : plan.rooms[o.b];
            var area = lift.id == o.a ? plan.rooms[o.b] : plan.rooms[o.a];
            if (!s.IsKnown(area.id)) continue;
            s.pois.Add(new MapPoi { kind = lift.id == plan.start ? PoiKind.ArrivalLift : PoiKind.ExitLift, x = lift.CenterX, z = lift.CenterZ, state = 0, room = area.id });
        }
    }

    // ------------------------------------------------------------------ il rasterizzatore
    sealed class Raster
    {
        readonly int W, H, S;
        readonly float[] c;            // RGB in virgola mobile, 3 per pixel, risoluzione S volte piu' alta
        public Raster(int w, int h, int ss) { W = w; H = h; S = ss; c = new float[W * S * H * S * 3]; }
        public float ppm(R2 view) => W / view.W;

        public void PaperBackdrop(PixelMap paper, R2 view)
        {
            int w = W * S, h = H * S;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = 1f - (y + 0.5f) / h;
                    paper.Sample(u, v, false, out float r, out float g, out float b, out float a);
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float light = 0.92f - 1.15f * (dx * dx + dy * dy);
                    int i = (y * w + x) * 3;
                    c[i] = r * light * 0.92f; c[i + 1] = g * light * 0.92f; c[i + 2] = b * light * 0.92f;
                }
        }

        public void Vignette()
        {
            int w = W * S, h = H * S;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w - 0.5f, v = (y + 0.5f) / h - 0.5f;
                    float k = 1f - 0.9f * Math.Max(0f, (u * u + v * v) * 4f - 0.55f);
                    k = Math.Max(0.2f, Math.Min(1f, k));
                    int i = (y * w + x) * 3;
                    c[i] *= k; c[i + 1] *= k; c[i + 2] *= k;
                }
        }

        public void LabelMark(MapLabel l, R2 view)
        {
            float ppm = W / view.W * S;
            float px = (l.pos.x - view.x0) * ppm, py = (view.y1 - l.pos.y) * ppm;
            int len = Math.Min(40, l.text.Length);
            float cw = l.kind == LabelKind.Room ? 0.55f * 4.2f : l.kind == LabelKind.Question ? 4f : 0.5f * 2.4f;
            float hw = len * cw * ppm * 0.5f * (l.kind == LabelKind.Room ? 1f : 0.8f), hh = (l.kind == LabelKind.Room ? 0.9f : l.kind == LabelKind.Question ? 3f : 0.55f) * ppm;
            Col col = l.kind == LabelKind.Price ? MapStyle.Red : l.kind == LabelKind.Question ? Col.F(0.8f, 0.7f, 0.5f) : MapStyle.Ink;
            float alpha = l.kind == LabelKind.Room ? 0.38f : 0.5f;
            for (int y = (int)(py - hh * 0.35f); y < (int)(py + hh * 0.35f); y++)
                for (int x = (int)(px - hw); x < (int)(px + hw); x++)
                {
                    if (x < 0 || y < 0 || x >= W * S || y >= H * S) continue;
                    Blend(x, y, col.r / 255f, col.g / 255f, col.b / 255f, alpha);
                }
        }

        void Blend(int x, int y, float r, float g, float b, float a)
        {
            int i = (y * W * S + x) * 3;
            c[i] += (r - c[i]) * a; c[i + 1] += (g - c[i + 1]) * a; c[i + 2] += (b - c[i + 2]) * a;
        }

        public void Mesh(Mesh2D m, R2 view, PixelMap tex, bool wrapTex)
        {
            float ppm = W / view.W * S;
            for (int t = 0; t + 2 < m.idx.Count; t += 3)
            {
                int i0 = m.idx[t], i1 = m.idx[t + 1], i2 = m.idx[t + 2];
                Tri(m, i0, i1, i2, view, ppm, tex, wrapTex);
            }
        }

        void Tri(Mesh2D m, int i0, int i1, int i2, R2 view, float ppm, PixelMap tex, bool wrap)
        {
            V2 p0 = ToPx(m.pos[i0], view, ppm), p1 = ToPx(m.pos[i1], view, ppm), p2 = ToPx(m.pos[i2], view, ppm);
            int minX = (int)Math.Floor(Math.Min(p0.x, Math.Min(p1.x, p2.x))), maxX = (int)Math.Ceiling(Math.Max(p0.x, Math.Max(p1.x, p2.x)));
            int minY = (int)Math.Floor(Math.Min(p0.y, Math.Min(p1.y, p2.y))), maxY = (int)Math.Ceiling(Math.Max(p0.y, Math.Max(p1.y, p2.y)));
            minX = Math.Max(0, minX); minY = Math.Max(0, minY); maxX = Math.Min(W * S - 1, maxX); maxY = Math.Min(H * S - 1, maxY);
            float area = V2.Cross(p1 - p0, p2 - p0);
            if (Math.Abs(area) < 1e-6f) return;
            float inv = 1f / area;
            Col c0 = m.col[i0], c1 = m.col[i1], c2 = m.col[i2];
            V2 u0 = m.uv[i0], u1 = m.uv[i1], u2 = m.uv[i2];
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    V2 p = new V2(x + 0.5f, y + 0.5f);
                    float w0 = V2.Cross(p2 - p1, p - p1) * inv, w1 = V2.Cross(p0 - p2, p - p2) * inv, w2 = 1f - w0 - w1;
                    if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                    float u = u0.x * w0 + u1.x * w1 + u2.x * w2, v = u0.y * w0 + u1.y * w1 + u2.y * w2;
                    tex.Sample(u, v, wrap, out float tr, out float tg, out float tb, out float ta);
                    float r = (c0.r * w0 + c1.r * w1 + c2.r * w2) / 255f * tr;
                    float g = (c0.g * w0 + c1.g * w1 + c2.g * w2) / 255f * tg;
                    float b = (c0.b * w0 + c1.b * w1 + c2.b * w2) / 255f * tb;
                    float a = (c0.a * w0 + c1.a * w1 + c2.a * w2) / 255f * ta;
                    if (a <= 0.003f) continue;
                    Blend(x, y, r, g, b, a);
                }
        }

        static V2 ToPx(V2 p, R2 view, float ppm) => new V2((p.x - view.x0) * ppm, (view.y1 - p.y) * ppm);

        public byte[] Png()
        {
            // media dei blocchi S x S, poi gamma 1 (i colori sono gia' quelli dell'interfaccia)
            var raw = new byte[(W * 3 + 1) * H];
            int w = W * S;
            for (int y = 0; y < H; y++)
            {
                raw[y * (W * 3 + 1)] = 0;
                for (int x = 0; x < W; x++)
                    for (int k = 0; k < 3; k++)
                    {
                        float sum = 0f;
                        for (int j = 0; j < S; j++) for (int i = 0; i < S; i++) sum += c[(((y * S + j) * w) + x * S + i) * 3 + k];
                        raw[y * (W * 3 + 1) + 1 + x * 3 + k] = (byte)Math.Max(0, Math.Min(255, (int)(sum / (S * S) * 255f + 0.5f)));
                    }
            }
            return EncodePng(W, H, raw);
        }
    }

    static byte[] EncodePng(int w, int h, byte[] rawScanlines)
    {
        using (var ms = new MemoryStream())
        {
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            void Chunk(string type, byte[] data)
            {
                var len = BitConverter.GetBytes(data.Length); Array.Reverse(len); ms.Write(len, 0, 4);
                var tb = System.Text.Encoding.ASCII.GetBytes(type); ms.Write(tb, 0, 4); ms.Write(data, 0, data.Length);
                uint crc = Crc(tb, data); var cb = BitConverter.GetBytes(crc); Array.Reverse(cb); ms.Write(cb, 0, 4);
            }
            var ihdr = new byte[13];
            var wb = BitConverter.GetBytes(w); Array.Reverse(wb); Array.Copy(wb, 0, ihdr, 0, 4);
            var hb = BitConverter.GetBytes(h); Array.Reverse(hb); Array.Copy(hb, 0, ihdr, 4, 4);
            ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
            Chunk("IHDR", ihdr);
            using (var zs = new MemoryStream())
            {
                using (var z = new ZLibStream(zs, CompressionLevel.Fastest, true)) z.Write(rawScanlines, 0, rawScanlines.Length);
                Chunk("IDAT", zs.ToArray());
            }
            Chunk("IEND", new byte[0]);
            return ms.ToArray();
        }
    }

    static uint[] crcTable;
    static uint Crc(byte[] type, byte[] data)
    {
        if (crcTable == null)
        {
            crcTable = new uint[256];
            for (uint n = 0; n < 256; n++) { uint cc = n; for (int k = 0; k < 8; k++) cc = (cc & 1) != 0 ? 0xEDB88320u ^ (cc >> 1) : cc >> 1; crcTable[n] = cc; }
        }
        uint c = 0xFFFFFFFFu;
        foreach (var b in type) c = crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    // le texture come immagini, per guardarle
    public static int DumpTextures(string dir)
    {
        Directory.CreateDirectory(dir);
        void Save(string name, PixelMap t, bool onGray)
        {
            var raw = new byte[(t.w * 3 + 1) * t.h];
            for (int y = 0; y < t.h; y++)
                for (int x = 0; x < t.w; x++)
                {
                    int src = ((t.h - 1 - y) * t.w + x) * 4, dst = y * (t.w * 3 + 1) + 1 + x * 3;
                    float a = t.rgba[src + 3] / 255f;
                    float bg = onGray ? 0.45f : 0f;
                    for (int k = 0; k < 3; k++) raw[dst + k] = (byte)Math.Max(0, Math.Min(255, (int)((t.rgba[src + k] / 255f * a + bg * (1f - a)) * 255f)));
                }
            File.WriteAllBytes(Path.Combine(dir, name), EncodePng(t.w, t.h, raw));
        }
        Save("paper.png", MapTextures.PaperTex(), false);
        Save("wash.png", MapTextures.WashTex(), true);
        Save("stains.png", MapTextures.StainAtlas(), true);
        Save("grain.png", MapTextures.GrainTex(), true);
        return 0;
    }
}
