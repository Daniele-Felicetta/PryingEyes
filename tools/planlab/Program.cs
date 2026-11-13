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
        // "old" in qualunque posizione: le ali d'albergo e di chiesa di prima, al posto della griglia
        if (Array.IndexOf(a, "old") >= 0) { PlanGen.UseGrid = false; a = Array.FindAll(a, x => x != "old"); }
        string cmd = a.Length > 0 ? a[0] : "stats";
        if (cmd == "survival") return Survival(a.Length > 1 ? int.Parse(a[1]) : 1000);
        if (cmd == "grid") return GridStats(a.Length > 1 ? int.Parse(a[1]) : 1000);
        if (cmd == "dump") return Dump(int.Parse(a[1]), int.Parse(a[2]));
        if (cmd == "passages") return Passages();
        if (cmd == "regions") return Regions(a.Length > 1 ? int.Parse(a[1]) : 1000);
        if (cmd == "stats") return Stats(a.Length > 1 ? int.Parse(a[1]) : 1000);
        if (cmd == "map") { Map(int.Parse(a[1]), int.Parse(a[2]), a[3]); return 0; }
        if (cmd == "sheet") { Sheet(int.Parse(a[1]), int.Parse(a[2]), a[3]); return 0; }
        return 1;
    }

    static int Survival(int count)
    {
        PlanGen.SurvivalLayout = true;
        int mainRooms = 0, atria = 0, purchases = 0, maxLinks = 0, maxPairs = 0, maxBossDepth = 0, maxExitCost = 0; double totalArea = 0, sumBossDepth = 0, sumExitCost = 0;
        void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        for (int i = 0; i < count; i++)
        {
            var plan = PlanGen.Generate(9000 + i * 31, 1 + i % 12);
            Check(plan != null, "Piano non generato: " + PlanGen.LastFailure);
            var rooms = plan.rooms.FindAll(r => !r.IsLift);
            Check(rooms.Count >= 7 && rooms.Count <= 9, "Numero di aree principali");
            Check(rooms.FindAll(r => r.GalleryLevels == 2).Count >= 2, "Mancano due atri a tre quote");
            Check(!plan.rooms.Exists(r => r.type == RoomType.Segreta), "Passaggio furtivo nel nuovo ciclo");
            var access = new RoomAccessState(plan);
            Check(plan.rooms[access.StartRoom].W * plan.rooms[access.StartRoom].D >= 60000, "Area iniziale troppo piccola");
            Check(access.IsOpen(access.StartRoom) && access.IsOpen(plan.start), "Arrivo chiuso");
            while (true)
            {
                int from = -1, next = -1;
                foreach (var aRoom in rooms)
                    foreach (var bRoom in rooms)
                        if (access.CanBuy(aRoom.id, bRoom.id)) { from = aRoom.id; next = bRoom.id; break; }
                if (next < 0) break;
                int cost = access.Price(next), before = access.OpenRooms.Count;
                Check(cost >= 500 && cost <= 1500, "Prezzo area fuori scala");
                Check(!access.TryBuy(from, next, cost - 1, out int paid) && paid == 0 && access.OpenRooms.Count == before, "Acquisto senza punti");
                Check(!access.TryBuy(next, from, 100000, out paid) && paid == 0, "Acquisto da area chiusa");
                Check(access.TryBuy(from, next, cost, out paid) && paid == cost && access.IsOpen(next), "Acquisto valido rifiutato");
                Check(!access.TryBuy(from, next, 100000, out paid) && paid == 0, "Doppio pagamento");
                var replica = new RoomAccessState(plan); replica.ApplySnapshot(access.OpenRooms);
                foreach (var room in plan.rooms) Check(replica.IsOpen(room.id) == access.IsOpen(room.id), "Snapshot incoerente");
                purchases++;
            }
            foreach (var room in plan.rooms) Check(access.IsOpen(room.id), "Area rimasta inaccessibile");

            // una porta per apertura, un'area della navmesh per coppia di stanze (RoomAccessDoors.PrepareNav): le aree sono 29 (3..31)
            var pairs = new HashSet<long>(); int links = 0;
            var adjacent = new List<int>[plan.rooms.Count];
            for (int k = 0; k < adjacent.Length; k++) adjacent[k] = new List<int>();
            foreach (var o in plan.openings)
            {
                if (!RoundRegions.IsLink(o)) continue;
                adjacent[o.a].Add(o.b); adjacent[o.b].Add(o.a);
                if (o.style == OpenStyle.Ascensore) continue;
                links++; pairs.Add(Math.Min(o.a, o.b) * 1000L + Math.Max(o.a, o.b));
            }
            maxLinks = Math.Max(maxLinks, links); maxPairs = Math.Max(maxPairs, pairs.Count);
            Check(pairs.Count <= 29, "Troppe coppie di stanze per le aree della navmesh");

            // quanto costa arrivare all'ascensore di uscita (la stanza del boss) comprando solo le aree sul cammino piu' corto
            var depth = new int[plan.rooms.Count]; Array.Fill(depth, -1);
            var queue = new Queue<int>(); queue.Enqueue(access.StartRoom); depth[access.StartRoom] = 0;
            while (queue.Count > 0) { int r0 = queue.Dequeue(); foreach (int n0 in adjacent[r0]) if (depth[n0] < 0) { depth[n0] = depth[r0] + 1; queue.Enqueue(n0); } }
            var boss = plan.OfType(RoomType.Boss);
            Check(boss.Count == 1, "Serve una sola stanza del boss");
            int bossDepth = depth[boss[0].id], exitCost = 0;
            for (int d = 1; d <= bossDepth; d++) exitCost += 500 + Math.Min(4, d - 1) * 250;
            sumBossDepth += bossDepth; sumExitCost += exitCost; maxBossDepth = Math.Max(maxBossDepth, bossDepth); maxExitCost = Math.Max(maxExitCost, exitCost);

            mainRooms += rooms.Count; atria += rooms.FindAll(r => r.GalleryLevels == 2).Count;
            foreach (var room in rooms) totalArea += room.W * room.D * 0.01;
        }
        Console.WriteLine($"SURVIVAL: {count} piani verificati, {mainRooms / (double)count:F1} aree principali medie, {totalArea / mainRooms:F0} m2 per area, {atria} atri a tre quote, {purchases} acquisti e snapshot validi");
        Console.WriteLine($"          porte al massimo {maxLinks}, coppie di stanze (aree navmesh) al massimo {maxPairs} su 29; ascensore di uscita a {sumBossDepth / count:F1} porte in media (massimo {maxBossDepth}), " +
                          $"costo per arrivarci {sumExitCost / count:F0} punti in media (massimo {maxExitCost})");
        return 0;
    }

    // Le stanze (ruolo, funzione, rettangolo in dm) e le aperture di un piano, a testo: per capire una prova fallita senza aprire Unity.
    //   dotnet run -c Release -- dump <seme del piano> <piano>      (il seme del piano e' quello che il gioco scrive: "piano 1 seme 4059")
    static int Dump(int seed, int floor)
    {
        var p = PlanGen.Generate(seed, floor);
        if (p == null) { Console.WriteLine("piano non generato: " + PlanGen.LastFailure); return 1; }
        Console.WriteLine($"piano {floor} seme {p.seed}: tema {p.theme}, {p.rooms.Count} stanze, arrivo = stanza {p.start}, uscita = stanza {p.exit}, griglia {p.grid}");
        foreach (var r in p.rooms)
            Console.WriteLine($"  stanza {r.id,2} {r.type,-10} {r.kind,-12} x {r.x0}..{r.x1} z {r.z0}..{r.z1}  ({r.W / 10f:F1} x {r.D / 10f:F1} m)");
        foreach (var o in p.openings)
        {
            if (o.b < 0) continue;
            Console.WriteLine($"  apertura {o.a,2}-{o.b,-2} {o.style,-10} {(o.alongX ? "lungo x" : "lungo z")} linea {o.line} centro {o.center} larga {o.width / 10f:F1} m");
        }
        return 0;
    }

    static int Passages()
    {
        for (int i = 0; i < 300; i++)
        {
            var p = PlanGen.Generate(1000 + i * 17, 1 + i % 12);
            if (p == null) throw new Exception("Generazione fallita");
            var passages = p.openings.FindAll(o => o.style == OpenStyle.Passaggio);
            if (passages.Count == 0) throw new Exception("Nessun passaggio: " + p.seed);
            foreach (var o in passages)
            {
                if (o.top != 1.3f || o.width != 12 || o.sill != 0f) throw new Exception("Dimensioni del passaggio");
                foreach (var other in p.openings)
                    if (o != other && o.line == other.line && o.alongX == other.alongX && o.Lo < other.Hi && o.Hi > other.Lo)
                        throw new Exception("Aperture sovrapposte");
            }
            p.openings.RemoveAll(o => o.style == OpenStyle.Passaggio);
            if (!PlanGen.Validate(p, out string why)) throw new Exception("Percorso ordinario compromesso: " + why);
        }
        Console.WriteLine("300 piani: passaggi bassi, senza sovrapposizioni e percorsi ordinari validi");
        return 0;
    }

    // Zone sigillabili intorno alle candele (round): quante candele ne hanno una, di che misura, con quante serrande.
    //   dotnet run -c Release -- regions 2000
    static int Regions(int n)
    {
        int plans = 0, candles = 0, withRegion = 0, plansAllCovered = 0, plansNone = 0;
        var why = new Dictionary<string, int>();
        var cutsHist = new int[RoundRegions.MaxCuts + 1]; var roomsHist = new int[RoundRegions.MaxRooms + 1];
        var areas = new List<float>(); var widths = new List<float>();
        var byTheme = new Dictionary<Theme, (int c, int r)>();
        var byFloor = new Dictionary<int, (int c, int r)>();
        var styleCuts = new Dictionary<OpenStyle, int>();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            int seed = 1000 + i * 17, floor = 1 + i % 12;
            var p = PlanGen.Generate(seed, floor);
            if (p == null) continue;
            plans++;
            var regs = RoundRegions.Find(p, why);
            candles += p.objectives.Count; withRegion += regs.Count;
            if (regs.Count == p.objectives.Count && p.objectives.Count > 0) plansAllCovered++;
            if (regs.Count == 0) plansNone++;
            (int c, int r) t = byTheme.TryGetValue(p.theme, out var tv) ? tv : (0, 0); byTheme[p.theme] = (t.c + p.objectives.Count, t.r + regs.Count);
            (int c, int r) f = byFloor.TryGetValue(floor, out var fv) ? fv : (0, 0); byFloor[floor] = (f.c + p.objectives.Count, f.r + regs.Count);
            foreach (var r in regs)
            {
                cutsHist[r.cuts.Count]++; roomsHist[r.rooms.Count]++; areas.Add(r.area);
                foreach (var o in r.cuts) { widths.Add(o.width * 0.1f); styleCuts[o.style] = styleCuts.TryGetValue(o.style, out int c) ? c + 1 : 1; }
            }
        }
        sw.Stop();
        areas.Sort(); widths.Sort();
        float Pct(List<float> l, double q) => l.Count == 0 ? 0 : l[Math.Min(l.Count - 1, (int)(l.Count * q))];
        Console.WriteLine($"{plans} piani in {sw.ElapsedMilliseconds} ms: {candles} candele, {withRegion} con una zona ({withRegion * 100.0 / Math.Max(1, candles):F0}%)");
        Console.WriteLine($"piani con tutte le candele coperte: {plansAllCovered * 100 / Math.Max(1, plans)}%, piani senza nessuna zona: {plansNone * 100 / Math.Max(1, plans)}%");
        Console.Write("serrande per zona: "); for (int k = 1; k < cutsHist.Length; k++) Console.Write($"{k}:{cutsHist[k]} "); Console.WriteLine();
        Console.Write("stanze per zona: "); for (int k = 1; k < roomsHist.Length; k++) Console.Write($"{k}:{roomsHist[k]} "); Console.WriteLine();
        Console.WriteLine($"area m2: min {Pct(areas, 0):F0}, 10% {Pct(areas, 0.1):F0}, mediana {Pct(areas, 0.5):F0}, 90% {Pct(areas, 0.9):F0}, max {areas[areas.Count - 1]:F0}");
        Console.WriteLine($"luce di una serranda m: mediana {Pct(widths, 0.5):F1}, 90% {Pct(widths, 0.9):F1}, max {widths[widths.Count - 1]:F1}");
        foreach (var kv in styleCuts) Console.WriteLine($"  serrande su {kv.Key}: {kv.Value}");
        foreach (var kv in byTheme) Console.WriteLine($"  tema {kv.Key}: {kv.Value.r}/{kv.Value.c} candele con zona ({kv.Value.r * 100 / Math.Max(1, kv.Value.c)}%)");
        for (int fl = 1; fl <= 12; fl++) if (byFloor.TryGetValue(fl, out var v2)) Console.WriteLine($"  piano {fl}: {v2.r}/{v2.c} ({v2.r * 100 / Math.Max(1, v2.c)}%)");
        foreach (var kv in why) Console.WriteLine($"  scartate: {kv.Key} x{kv.Value}");
        return 0;
    }

    // Piante a griglia (fase 4): quanti piani riescono, quante stanze e di che tipo, porte per stanza, zone dei round, ascensori.
    //   dotnet run -c Release -- grid 2000
    static int GridStats(int n)
    {
        int fail = 0, plans = 0, bad = 0;
        var sw = Stopwatch.StartNew();
        var attempts = new int[81]; var reasons = new Dictionary<string, int>();
        double rooms = 0, area = 0, windows = 0, walk = 0, hops = 0;
        var degHist = new int[8]; var types = new Dictionary<RoomType, int>(); var kinds = new Dictionary<RoomKind, int>();
        var cutsHist = new int[8]; var cells = new Dictionary<string, int>(); var themeN = new Dictionary<Theme, int>();
        double maxW = 0, maxD = 0, minArena = 1e9, maxArena = 0, arenaSum = 0; int arenaN = 0;
        var secretLinks = 0; int secretN = 0, passages = 0;
        for (int i = 0; i < n; i++)
        {
            int seed = 1000 + i * 17, floor = 1 + i % 12;
            var p = PlanGen.Generate(seed, floor);
            if (p == null) { fail++; string r = PlanGen.LastFailure; reasons[r] = reasons.TryGetValue(r, out int c) ? c + 1 : 1; continue; }
            plans++; attempts[PlanGen.LastAttempts]++;
            themeN[p.theme] = themeN.TryGetValue(p.theme, out int tn) ? tn + 1 : 1;
            if (!PlanGen.Validate(p, out string why) || !PlanGen.ValidateGrid(p, out why)) { bad++; Console.WriteLine($"seme {seed} piano {floor}: {why}"); }
            foreach (var o in p.openings) { if (o.b < 0 && !o.oneSided) windows++; if (o.style == OpenStyle.Passaggio) passages++; }
            int real = 0;
            foreach (var r in p.rooms)
            {
                if (r.IsLift) continue;
                real++;
                area += r.W * r.D / 100.0;
                int d = p.DoorCount(r); degHist[Math.Min(7, d)]++;
                types[r.type] = types.TryGetValue(r.type, out int tc) ? tc + 1 : 1;
                kinds[r.kind] = kinds.TryGetValue(r.kind, out int kc) ? kc + 1 : 1;
                int cw = (int)Math.Round(r.W / 96.0), ch = (int)Math.Round(r.D / 96.0);
                string key = cw + "x" + ch; cells[key] = cells.TryGetValue(key, out int cc) ? cc + 1 : 1;
                if (r.type == RoomType.Segreta) { secretN++; foreach (var o in p.openings) if ((o.a == r.id || o.b == r.id) && o.b >= 0 && o.style != OpenStyle.Ascensore && !o.oneSided) secretLinks++; }
            }
            rooms += real;
            maxW = Math.Max(maxW, (p.maxX - p.minX) / 10.0); maxD = Math.Max(maxD, (p.maxZ - p.minZ) / 10.0);
            walk += p.WalkDist(p.start)[p.exit]; hops += p.Hops(p.start)[p.exit];
            foreach (var reg in RoundRegions.Find(p)) { cutsHist[Math.Min(7, reg.cuts.Count)]++; arenaN++; arenaSum += reg.area; minArena = Math.Min(minArena, reg.area); maxArena = Math.Max(maxArena, reg.area); }
        }
        sw.Stop();
        int ok = Math.Max(1, plans);
        Console.WriteLine($"{n} piani in {sw.ElapsedMilliseconds} ms ({sw.ElapsedMilliseconds / (double)n:F2} ms/piano), falliti {fail}, controlli falliti {bad}");
        Console.Write("tentativi: "); for (int i = 1; i <= 80; i++) if (attempts[i] > 0) Console.Write($"{i}:{attempts[i]} "); Console.WriteLine();
        foreach (var kv in reasons) Console.WriteLine($"  motivo: {kv.Key} x{kv.Value}");
        Console.WriteLine($"stanze {rooms / ok:F1} per piano, area {area / ok:F0} m2, finestre {windows / ok:F1}, ingombro massimo {maxW:F0} x {maxD:F0} m, cammino arrivo-uscita {walk / ok:F0} m ({hops / ok:F1} porte)");
        Console.Write("porte per stanza: "); for (int k = 0; k < degHist.Length; k++) if (degHist[k] > 0) Console.Write($"{k}:{degHist[k] * 100.0 / rooms:F0}% "); Console.WriteLine();
        Console.Write("ruoli per piano: "); foreach (var kv in types) Console.Write($"{kv.Key} {kv.Value / (double)ok:F2}  "); Console.WriteLine();
        Console.Write("misure (celle): "); foreach (var kv in cells) Console.Write($"{kv.Key} {kv.Value * 100.0 / rooms:F0}%  "); Console.WriteLine();
        Console.WriteLine($"stanze segrete: {secretN}, con una sola via ({secretLinks} collegamenti), passaggi bassi in tutto {passages / (double)ok:F1} per piano");
        Console.Write("serrande per arena: "); for (int k = 1; k < cutsHist.Length; k++) if (cutsHist[k] > 0) Console.Write($"{k}:{cutsHist[k]} "); Console.WriteLine();
        Console.WriteLine($"arene: {arenaN} (media {arenaSum / Math.Max(1, arenaN):F0} m2, min {minArena:F0}, max {maxArena:F0})");
        foreach (var kv in themeN) Console.WriteLine($"  tema {kv.Key}: {kv.Value} piani");
        Console.Write("funzioni: "); foreach (var kv in kinds) Console.Write($"{kv.Key} {kv.Value / (double)ok:F2}  "); Console.WriteLine();
        return fail == 0 && bad == 0 ? 0 : 2;
    }

    static (byte r, byte g, byte b) RoomCol(Plan p, Room r)
    {
        if (!p.grid || r.IsLift) return Col[r.kind];
        switch (r.type)
        {
            case RoomType.Partenza: return (90, 190, 110);
            case RoomType.Boss: return (190, 60, 60);
            case RoomType.Candela: return (225, 140, 60);
            case RoomType.Negozio: return (230, 200, 90);
            case RoomType.Biblioteca: return (150, 110, 220);
            case RoomType.Segreta: return (80, 200, 200);
            default: return (110, 125, 145);
        }
    }

    static int Stats(int n)
    {
        int fail = 0; var attempts = new int[81]; var sw = Stopwatch.StartNew();
        double rooms = 0, area = 0; int maxRooms = 0, minRooms = 999;
        var kinds = new Dictionary<RoomKind, int>(); int churchStart = 0, openings = 0;
        var themeN = new Dictionary<Theme, int>(); var themeRooms = new Dictionary<Theme, int>();
        var themeAtt = new Dictionary<Theme, double>(); var themeWalk = new Dictionary<Theme, double>();
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
            themeN[p.theme] = themeN.TryGetValue(p.theme, out int tn) ? tn + 1 : 1;
            themeRooms[p.theme] = themeRooms.TryGetValue(p.theme, out int tr) ? tr + p.rooms.Count : p.rooms.Count;
            themeAtt[p.theme] = themeAtt.TryGetValue(p.theme, out double ta) ? ta + PlanGen.LastAttempts : PlanGen.LastAttempts;
            themeWalk[p.theme] = themeWalk.TryGetValue(p.theme, out double tw) ? tw + p.WalkDist(p.start)[p.exit] : p.WalkDist(p.start)[p.exit];
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
        foreach (var kv in themeN)
            Console.WriteLine($"  tema {kv.Key}: {kv.Value} piani, {themeRooms[kv.Key] / (double)kv.Value:F0} stanze in media, tentativi medi {themeAtt[kv.Key] / kv.Value:F1}, cammino medio {themeWalk[kv.Key] / kv.Value:F0} m");
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
        { RoomKind.Platea, (110, 30, 40) }, { RoomKind.Palco, (150, 60, 70) }, { RoomKind.Foyer, (200, 150, 90) },
        { RoomKind.Palcoscenico, (120, 60, 130) }, { RoomKind.Camerino, (170, 110, 150) }, { RoomKind.Deposito, (90, 90, 90) }, { RoomKind.Ridotto, (190, 120, 60) },
        { RoomKind.Accettazione, (180, 200, 210) }, { RoomKind.Degenza, (200, 210, 220) }, { RoomKind.Corsia, (160, 190, 200) },
        { RoomKind.Cella, (120, 130, 140) }, { RoomKind.Refettorio, (150, 170, 150) }, { RoomKind.SalaOperatoria, (220, 240, 245) },
        { RoomKind.Lavanderia, (120, 160, 170) }, { RoomKind.Archivio, (140, 120, 90) }, { RoomKind.Obitorio, (70, 90, 100) },
        { RoomKind.SalaMedici, (170, 170, 120) },
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
            Fill(r.x0, r.z0, r.x1, r.z1, RoomCol(p, r));
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
        Console.WriteLine($"seme {seed} piano {floor}: {p.rooms.Count} stanze, {p.openings.Count} aperture, tentativi {PlanGen.LastAttempts}, {(p.maxX - p.minX) / 10.0:F0}x{(p.maxZ - p.minZ) / 10.0:F0} m, partenza {(p.startInChurch ? "chiesa" : "albergo")}, tema {p.theme}, cammino {p.WalkDist(p.start)[p.exit]:F0} m");
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
