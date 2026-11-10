using System;
using System.Collections.Generic;

namespace PryingEyes.Proc
{
    /// <summary>Generatore deterministico (SplitMix64): stesso seme, stessa pianta, su qualunque macchina. Non usa
    /// UnityEngine.Random: la pianta deve poter essere riprodotta e testata fuori dal gioco.</summary>
    public sealed class Rng
    {
        ulong state;
        public Rng(int seed) { state = (ulong)(long)seed * 0x9E3779B97F4A7C15UL + 0xD1B54A32D192ED03UL; NextU64(); }
        public ulong NextU64()
        {
            ulong z = (state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public double Value() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);
        public int Range(int lo, int hi) => lo + (int)(Value() * (hi - lo));          // [lo, hi)
        public float Range(float lo, float hi) => lo + (float)Value() * (hi - lo);
        public bool Chance(double p) => Value() < p;
        public T Pick<T>(params T[] a) => a[Range(0, a.Length)];
        public Rng Fork() => new Rng((int)(NextU64() & 0x7fffffff));
        public int Weighted(double[] w)
        {
            double t = 0; foreach (var x in w) t += x;
            double v = Value() * t;
            for (int i = 0; i < w.Length; i++) { v -= w[i]; if (v < 0) return i; }
            return w.Length - 1;
        }
    }

    /// <summary>Genera la pianta di un piano: un'ala d'albergo (atrio, corridoio, camere, a volte ristorante o sala da
    /// ballo) e un'ala di chiesa (nartece, navata con navate laterali e cappelle, presbiterio, sacrestia, cripta),
    /// unite da un portale e orientate in modo diverso a ogni piano. Gli ascensori di arrivo e di uscita sono due
    /// stanzette attaccate ai muri esterni delle due ali.</summary>
    public static partial class PlanGen
    {
        public static string LastFailure = "";
        public static int LastAttempts;

        /// <summary>Le stanze sono un po' piu' grandi di prima (+12%): ogni misura di stanza passa da Grow. Il risultato e' pari.</summary>
        const float RoomGrow = 1.12f;
        static int Grow(int dm) => 2 * (int)Math.Round(dm * RoomGrow * 0.5f);
        /// <summary>Ingombro massimo di un piano intero (dm): oltre si scarta la pianta.</summary>
        const int MaxSpan = 2400;
        static readonly Side[] AllSides = { Side.S, Side.N, Side.W, Side.E };

        public static Plan Generate(int seed, int floor)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                var rng = new Rng(Hash(seed, floor, attempt));
                var p = Build(rng, seed, floor, simple: attempt >= 60);
                if (p == null) continue;
                if (Validate(p, out string why)) { LastAttempts = attempt + 1; return p; }
                LastFailure = why;
            }
            LastAttempts = 80;
            return null;
        }

        static int Hash(int seed, int floor, int attempt) =>
            unchecked(seed * 73856093 ^ floor * 19349663 ^ attempt * 83492791 ^ 0x5bd1e995);

        // ------------------------------------------------------------------ ali
        sealed class Wing
        {
            public readonly List<Room> rooms = new List<Room>();
            public readonly List<Opening> ops = new List<Opening>();
            public readonly bool church;
            public readonly int wing;
            public Wing(bool church, int wing = -1) { this.church = church; this.wing = wing >= 0 ? wing : (church ? 1 : 0); }

            public Room Add(RoomKind k, int x0, int z0, int x1, int z1, Rng r)
            {
                var room = new Room { id = rooms.Count, kind = k, church = church, wing = wing, x0 = x0, z0 = z0, x1 = x1, z1 = z1, seed = r.Range(1, 1 << 30) };
                SetKindDefaults(room);
                rooms.Add(room);
                return room;
            }

            public Opening Connect(Room a, Room b, OpenStyle st, int width, int at = int.MinValue, int margin = 3) =>
                PlanGen.Connect(ops, a, b, st, width, at, margin);
        }

        static void SetKindDefaults(Room r)
        {
            if (ThemeKindDefaults(r)) return;
            r.ceil = CeilKind.Flat;
            switch (r.kind)
            {
                case RoomKind.Atrio: r.wallH = 4.4f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Corridoio: r.wallH = 3.1f; break;
                case RoomKind.Camera: r.wallH = 3.0f; break;
                case RoomKind.Suite: r.wallH = 3.15f; break;
                case RoomKind.Servizio: r.wallH = 2.9f; break;
                case RoomKind.Ristorante: r.wallH = 4.2f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Cucina: r.wallH = 3.4f; break;
                case RoomKind.SalaBallo: r.wallH = 5.4f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Nartece: r.wallH = 5.2f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Navata: r.wallH = 8.6f; r.ridgeH = 12.2f; r.ceil = CeilKind.Gable; r.ridgeAlongX = true; break;
                case RoomKind.Navatella: r.wallH = 6.2f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Cappella: r.wallH = 3.6f; r.ridgeH = 5.3f; r.ceil = CeilKind.BarrelRound; r.ridgeAlongX = false; break;
                case RoomKind.Presbiterio: r.wallH = 6.6f; r.ridgeH = 10.8f; r.ceil = CeilKind.BarrelPointed; r.ridgeAlongX = true; break;
                case RoomKind.Sacrestia: r.wallH = 3.4f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Cripta: r.wallH = 2.5f; r.ridgeH = 3.9f; r.ceil = CeilKind.BarrelRound; r.ridgeAlongX = true; break;
                case RoomKind.Campanile: r.wallH = 6.6f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Ascensore: r.wallH = 2.6f; break;
            }
            if (r.ceil == CeilKind.Flat || r.ceil == CeilKind.Beams) r.ridgeH = r.wallH;
        }

        /// <summary>Apre una porta (o un'arcata) nel muro condiviso tra a e b. Il centro e' 'at' (dm lungo il muro) o
        /// il mezzo del tratto in comune; nullo se il muro non basta.</summary>
        static Opening Connect(List<Opening> ops, Room a, Room b, OpenStyle st, int width, int at, int margin)
        {
            bool alongX; int line, lo, hi;
            if (a.x1 == b.x0) { alongX = false; line = a.x1; lo = Math.Max(a.z0, b.z0); hi = Math.Min(a.z1, b.z1); }
            else if (a.x0 == b.x1) { alongX = false; line = a.x0; lo = Math.Max(a.z0, b.z0); hi = Math.Min(a.z1, b.z1); }
            else if (a.z1 == b.z0) { alongX = true; line = a.z1; lo = Math.Max(a.x0, b.x0); hi = Math.Min(a.x1, b.x1); }
            else if (a.z0 == b.z1) { alongX = true; line = a.z0; lo = Math.Max(a.x0, b.x0); hi = Math.Min(a.x1, b.x1); }
            else return null;
            width &= ~1;
            int room = hi - lo - 2 * margin;
            if (room < 8) return null;
            if (width > room) width = room & ~1;
            int c = at == int.MinValue ? (lo + hi) / 2 : at;
            c = Math.Max(lo + margin + width / 2, Math.Min(hi - margin - width / 2, c));
            var o = new Opening { a = a.id, b = b.id, alongX = alongX, line = line, center = c, width = width, style = st };
            Opening.Defaults(o);
            ops.Add(o);
            return o;
        }

        // ------------------------------------------------------------------ ala d'albergo
        static Wing HotelWing(Rng r, bool simple, HotelKinds kd)
        {
            var w = new Wing(false, 0);
            int cw = Grow(r.Pick(24, 26, 28));
            int L = simple ? Grow(280) : Grow(r.Range(23, 39) * 10);
            var cor = w.Add(RoomKind.Corridoio, 0, 0, L, cw, r);
            int pad = Grow(r.Pick(24, 30, 36));
            var atr = w.Add(kd.Atrium, -Grow(r.Pick(70, 80, 90)), -pad, 0, cw + pad, r);
            w.Connect(atr, cor, OpenStyle.PortaLarga, cw, margin: 0);

            for (int side = 0; side < 2; side++)
            {
                bool north = side == 0;
                // larghezze: camere da 3,6-4,8 m; l'ultima prende il resto
                var widths = new List<int>();
                int rem = L;
                while (rem > 0)
                {
                    int wd = Grow(r.Pick(36, 42, 48, 42));
                    if (rem - wd < Grow(30) && rem - wd != 0) wd = rem <= Grow(66) ? rem : rem / 2;
                    wd &= ~1;
                    if (rem - wd < 0) wd = rem;
                    widths.Add(wd); rem -= wd;
                }
                int guestD = Grow(r.Pick(54, 54, 60));
                // un blocco speciale per ala: ristorante (con cucina accanto) o sala da ballo, se c'e' posto
                int bigStart = -1, bigCount = 0;
                RoomKind bigKind = kd.Big;
                if (!simple && widths.Count >= 6 && r.Chance(north ? 0.55 : 0.25))
                {
                    bigKind = r.Chance(0.35) ? kd.BigAlt : kd.Big;
                    int need = Grow(bigKind == kd.BigAlt ? 120 : 96);
                    bigStart = r.Range(1, widths.Count - 2);
                    int sum = 0;
                    while (bigStart + bigCount < widths.Count - 1 && sum < need) { sum += widths[bigStart + bigCount]; bigCount++; }
                    if (sum < need) { bigStart = -1; bigCount = 0; }
                }
                int x = 0;
                Room prev = null;
                for (int i = 0; i < widths.Count; i++)
                {
                    if (i == bigStart)
                    {
                        int sum = 0; for (int k = 0; k < bigCount; k++) sum += widths[i + k];
                        int d = Grow(bigKind == kd.BigAlt ? r.Pick(100, 108) : r.Pick(84, 90, 96));
                        var big = AddSide(w, r, cor, north, bigKind, x, x + sum, d, cw);
                        w.Connect(big, cor, OpenStyle.PortaLarga, 16, x + sum / 2, 3);   // doppia porta sul corridoio
                        i += bigCount - 1;
                        x += sum;
                        prev = big;
                        // la cucina sta accanto al ristorante
                        if (bigKind == kd.Big && i + 1 < widths.Count && widths[i + 1] >= Grow(36) && widths[i + 1] <= Grow(66))
                        {
                            i++;
                            var kit = AddSide(w, r, cor, north, kd.Kitchen, x, x + widths[i], Math.Min(d, Grow(66)), cw);
                            w.Connect(kit, cor, OpenStyle.Porta, 14, x + widths[i] / 2, 3);
                            w.Connect(big, kit, OpenStyle.Porta, 14, int.MinValue, 4);
                            x += widths[i];
                            prev = kit;
                        }
                        continue;
                    }
                    int wd = widths[i];
                    RoomKind k2;
                    if (wd <= Grow(32)) k2 = r.Chance(0.75) ? kd.PickService(r) : kd.Guest;
                    else if (wd >= Grow(60)) k2 = kd.Suite;
                    else k2 = r.Chance(0.07) ? kd.PickService(r) : kd.Guest;
                    var room = AddSide(w, r, cor, north, k2, x, x + wd, k2 == kd.Suite ? kd.SuiteDepth : (IsService(k2) ? Grow(48) : guestD), cw);
                    int doorW = 14;     // a 1,2 m la navmesh era al limite (agente da 40 cm di raggio): ora 1,4 m ovunque
                    int dc = x + wd / 2 + (k2 == kd.Guest && wd >= Grow(42) ? r.Pick(-8, 8, 0) : 0);
                    w.Connect(room, cor, k2 == kd.Suite ? OpenStyle.PortaLarga : OpenStyle.Porta, doorW, dc, 3);
                    // porta di collegamento con la stanza accanto, ogni tanto: crea giri e scorciatoie
                    if (prev != null && prev.kind != kd.Kitchen && !IsBig(prev.kind) && !IsService(k2) && !IsService(prev.kind) && r.Chance(0.22))
                    {
                        int back = north ? cw + (int)(guestD * 0.7f) : -(int)(guestD * 0.7f);
                        w.Connect(prev, room, OpenStyle.Porta, 14, back, 3);
                    }
                    prev = room;
                    x += wd;
                }
            }
            return w;
        }

        static bool IsBig(RoomKind k) => k == RoomKind.Ristorante || k == RoomKind.SalaBallo || k == RoomKind.Refettorio || k == RoomKind.SalaOperatoria;

        /// <summary>Stanze di servizio: non ricevono le porte di collegamento tra le camere e non fanno da cucina.</summary>
        static bool IsService(RoomKind k) => k == RoomKind.Servizio || k == RoomKind.Cella || k == RoomKind.Archivio || k == RoomKind.Obitorio || k == RoomKind.SalaMedici || k == RoomKind.Lavanderia;

        static Room AddSide(Wing w, Rng r, Room cor, bool north, RoomKind k, int x0, int x1, int depth, int cw) =>
            north ? w.Add(k, x0, cw, x1, cw + depth, r) : w.Add(k, x0, -depth, x1, 0, r);

        // ------------------------------------------------------------------ ala di chiesa
        static Wing ChurchWing(Rng r, bool simple)
        {
            var w = new Wing(true);
            int bay = Grow(45);
            int bays = simple ? 4 : r.Range(4, 7);
            int Ln = bays * bay;
            int Wn = simple ? Grow(90) : Grow(r.Pick(84, 90, 96));
            int half = Wn / 2;
            bool aisles = simple || r.Chance(0.82);
            int Wa = Grow(36);
            int ext = aisles ? Wa : 0;

            var nave = w.Add(RoomKind.Navata, 0, -half, Ln, half, r);
            Room aN = null, aS = null;
            if (aisles)
            {
                aN = w.Add(RoomKind.Navatella, 0, half, Ln, half + Wa, r);
                aS = w.Add(RoomKind.Navatella, 0, -half - Wa, Ln, -half, r);
            }
            var nar = w.Add(RoomKind.Nartece, -Grow(54), -half - ext, 0, half + ext, r);
            int presD = Grow(r.Pick(72, 78, 84));
            var pres = w.Add(RoomKind.Presbiterio, Ln, -half, Ln + presD, half, r);
            var sac = w.Add(RoomKind.Sacrestia, Ln, half, Ln + Grow(54), half + Grow(54), r);
            var cry = w.Add(RoomKind.Cripta, Ln, -half - Grow(54), Ln + Grow(66), -half, r);

            // porte e arcate
            w.Connect(nar, nave, OpenStyle.Arco, 30, 0);
            if (aisles)
            {
                w.Connect(nar, aN, OpenStyle.Arco, 14, half + Wa / 2);
                w.Connect(nar, aS, OpenStyle.Arco, 14, -half - Wa / 2);
                for (int bi = 0; bi < bays; bi++)
                {
                    int cx = bi * bay + bay / 2;
                    w.Connect(nave, aN, OpenStyle.Arcata, 30, cx, 0);
                    w.Connect(nave, aS, OpenStyle.Arcata, 30, cx, 0);
                }
            }
            var chancel = w.Connect(nave, pres, OpenStyle.Arco, 54, 0);
            if (chancel != null) chancel.top = 8.2f;
            w.Connect(pres, sac, OpenStyle.Arco, 14, Ln + Grow(54) / 2);
            w.Connect(pres, cry, OpenStyle.Arco, 14, Ln + Grow(66) / 2);
            if (aisles)
            {
                w.Connect(aN, sac, OpenStyle.Arco, 14);
                w.Connect(aS, cry, OpenStyle.Arco, 14);
            }

            // cappelle laterali: una campata ciascuna, contro il muro esterno della navata laterale
            int maxCh = simple ? 1 : r.Range(1, 4);
            for (int sd = 0; sd < 2; sd++)
            {
                bool north = sd == 0;
                var taken = new HashSet<int>();
                int n = Math.Min(maxCh, bays);
                for (int k = 0; k < n; k++)
                {
                    int bi = r.Range(0, bays);
                    if (taken.Contains(bi) || taken.Contains(bi - 1) || taken.Contains(bi + 1)) continue;
                    taken.Add(bi);
                    int cz0 = north ? half + ext : -half - ext - Grow(48);
                    var cap = w.Add(RoomKind.Cappella, bi * bay, cz0, (bi + 1) * bay, cz0 + Grow(48), r);
                    var host = aisles ? (north ? aN : aS) : nave;
                    w.Connect(cap, host, OpenStyle.Arco, 24, bi * bay + bay / 2);
                }
            }
            // campanile: stanza alta accanto al nartece
            if (!simple && r.Chance(0.5))
            {
                var camp = w.Add(RoomKind.Campanile, -Grow(54), half + ext, 0, half + ext + Grow(54), r);
                w.Connect(nar, camp, OpenStyle.Arco, 14);
            }
            return w;
        }

        // ------------------------------------------------------------------ assemblaggio
        struct Xf
        {
            public bool mirror; public int rot; public int tx, tz;
            public void Apply(int x, int z, out int ox, out int oz)
            {
                if (mirror) x = -x;
                for (int i = 0; i < rot; i++) { int t = x; x = -z; z = t; }
                ox = x + tx; oz = z + tz;
            }
            public Side ApplySide(Side s)
            {
                int nx = s == Side.W ? -1 : s == Side.E ? 1 : 0, nz = s == Side.S ? -1 : s == Side.N ? 1 : 0;
                if (mirror) nx = -nx;
                for (int i = 0; i < rot; i++) { int t = nx; nx = -nz; nz = t; }
                return nx < 0 ? Side.W : nx > 0 ? Side.E : nz < 0 ? Side.S : Side.N;
            }
        }

        static void PortPoint(Room r, Side s, out int x, out int z)
        {
            Plan.Span(r, s, out int lo, out int hi);
            int c = (lo + hi) / 2;
            if (Plan.AlongX(s)) { x = c; z = Plan.Line(r, s); } else { x = Plan.Line(r, s); z = c; }
        }

        static Plan Assemble(Wing h, Wing c, Rng r, bool simple, Joint j)
        {
            var hp = new List<(RoomKind k, Side s, double wt)>(j.A);
            var cp = new List<(RoomKind k, Side s, double wt)>(j.B);
            if (simple) { hp.RemoveRange(1, hp.Count - 1); cp.RemoveRange(1, cp.Count - 1); }

            var cands = new List<(Room hr, Side hs, Room cr, Side cs, Xf xf, double wt)>();
            foreach (var a in hp)
            {
                Room hr = h.rooms.Find(q => q.kind == a.k);
                if (hr == null) continue;
                PortPoint(hr, a.s, out int hx, out int hz);
                foreach (var b in cp)
                {
                    Room cr = c.rooms.Find(q => q.kind == b.k);
                    if (cr == null) continue;
                    PortPoint(cr, b.s, out int cx, out int cz);
                    for (int m = 0; m < 2; m++)
                        for (int rot = 0; rot < 4; rot++)
                        {
                            var xf = new Xf { mirror = m == 1, rot = rot };
                            if (xf.ApplySide(b.s) != Plan.Opposite(a.s)) continue;
                            xf.Apply(cx, cz, out int px, out int pz);
                            xf.tx = hx - px; xf.tz = hz - pz;
                            if (!Fits(h, c, xf)) continue;
                            cands.Add((hr, a.s, cr, b.s, xf, a.wt * b.wt));
                        }
                }
            }
            if (cands.Count == 0) return null;
            var w = new double[cands.Count];
            for (int i = 0; i < w.Length; i++) w[i] = cands[i].wt;
            var pick = cands[r.Weighted(w)];

            var p = new Plan();
            foreach (var room in h.rooms) { room.id = p.rooms.Count; p.rooms.Add(room); }
            int off = p.rooms.Count;
            var map = new Dictionary<Room, Room>();
            foreach (var room in c.rooms)
            {
                var t = Transform(room, pick.xf);
                t.id = p.rooms.Count;
                map[room] = t;
                p.rooms.Add(t);
            }
            foreach (var o in h.ops) p.openings.Add(o);
            foreach (var o in c.ops)
            {
                var t = TransformOpening(o, pick.xf);
                t.a += off; if (t.b >= 0) t.b += off;
                p.openings.Add(t);
            }
            // il portale tra le due ali
            var link = Connect(p.openings, pick.hr, map[pick.cr], j.Style, j.Width, int.MinValue, 3);
            if (link == null) return null;
            p.startInChurch = r.Chance(0.5);
            return p;
        }

        static Room Transform(Room r, Xf xf)
        {
            xf.Apply(r.x0, r.z0, out int ax, out int az);
            xf.Apply(r.x1, r.z1, out int bx, out int bz);
            var t = new Room
            {
                id = r.id, kind = r.kind, church = r.church, wing = r.wing, seed = r.seed, label = r.label, variant = r.variant, unit = r.unit,
                x0 = Math.Min(ax, bx), x1 = Math.Max(ax, bx), z0 = Math.Min(az, bz), z1 = Math.Max(az, bz),
                wallH = r.wallH, ridgeH = r.ridgeH, ceil = r.ceil,
                ridgeAlongX = (xf.rot & 1) == 1 ? !r.ridgeAlongX : r.ridgeAlongX,
            };
            return t;
        }

        static Opening TransformOpening(Opening o, Xf xf)
        {
            int ax, az, bx, bz;
            if (o.alongX) { xf.Apply(o.Lo, o.line, out ax, out az); xf.Apply(o.Hi, o.line, out bx, out bz); }
            else { xf.Apply(o.line, o.Lo, out ax, out az); xf.Apply(o.line, o.Hi, out bx, out bz); }
            var t = new Opening
            {
                a = o.a, b = o.b, style = o.style, sill = o.sill, top = o.top, oneSided = o.oneSided, variant = o.variant, width = o.width,
            };
            if (ax == bx) { t.alongX = false; t.line = ax; t.center = (az + bz) / 2; }
            else { t.alongX = true; t.line = az; t.center = (ax + bx) / 2; }
            return t;
        }

        /// <summary>Le stanze trasformate dell'ala di chiesa non devono sovrapporsi a quelle dell'albergo (e viceversa).</summary>
        static bool Fits(Wing h, Wing c, Xf xf)
        {
            var tr = new List<Room>();
            foreach (var room in c.rooms) tr.Add(Transform(room, xf));
            foreach (var a in h.rooms)
                foreach (var b in tr)
                    if (Overlap(a, b, 0)) return false;
            return true;
        }

        static bool Overlap(Room a, Room b, int gap) =>
            a.x0 < b.x1 + gap && b.x0 < a.x1 + gap && a.z0 < b.z1 + gap && b.z0 < a.z1 + gap;

        // ------------------------------------------------------------------ ascensori
        static bool AddLift(Plan p, Rng r, Room host)
        {
            var sides = new List<Side> { Side.S, Side.N, Side.W, Side.E };
            for (int i = sides.Count - 1; i > 0; i--) { int j = r.Range(0, i + 1); var t = sides[i]; sides[i] = sides[j]; sides[j] = t; }
            const int Lw = 26;
            foreach (var s in sides)
            {
                foreach (var seg in p.Segments(host, s))
                {
                    if (seg.other >= 0 || seg.a1 - seg.a0 < Lw + 8) continue;
                    // posizione casuale ma dentro il tratto, lontana dagli angoli
                    int lo = seg.a0 + Lw / 2 + 4, hi = seg.a1 - Lw / 2 - 4;
                    int c = lo >= hi ? (seg.a0 + seg.a1) / 2 : r.Range(lo, hi + 1);
                    int x0, z0, x1, z1;
                    int line = Plan.Line(host, s);
                    if (s == Side.S) { x0 = c - Lw / 2; x1 = c + Lw / 2; z1 = line; z0 = line - Lw; }
                    else if (s == Side.N) { x0 = c - Lw / 2; x1 = c + Lw / 2; z0 = line; z1 = line + Lw; }
                    else if (s == Side.W) { z0 = c - Lw / 2; z1 = c + Lw / 2; x1 = line; x0 = line - Lw; }
                    else { z0 = c - Lw / 2; z1 = c + Lw / 2; x0 = line; x1 = line + Lw; }
                    var cab = new Room { kind = RoomKind.Ascensore, x0 = x0, z0 = z0, x1 = x1, z1 = z1, church = host.church, wing = host.wing, unit = host.unit, seed = r.Range(1, 1 << 30) };
                    SetKindDefaults(cab);
                    bool clash = false;
                    foreach (var o in p.rooms) if (Overlap(o, cab, 2) && o != host) { clash = true; break; }
                    if (clash) continue;
                    cab.id = p.rooms.Count;
                    p.rooms.Add(cab);
                    var op = Connect(p.openings, host, cab, OpenStyle.Ascensore, 14, c, 3);
                    if (op == null) { p.rooms.RemoveAt(p.rooms.Count - 1); continue; }
                    return true;
                }
            }
            return false;
        }

        static bool PlaceLifts(Plan p, Rng r)
        {
            RoomKind[] startKinds = StartKinds(p.theme, p.startInChurch);
            Room startHost = null;
            foreach (var k in startKinds)
            {
                var cands = p.rooms.FindAll(q => q.kind == k);
                for (int i = cands.Count - 1; i > 0; i--) { int j = r.Range(0, i + 1); var t = cands[i]; cands[i] = cands[j]; cands[j] = t; }
                foreach (var cr in cands) { if (AddLift(p, r, cr)) { startHost = cr; break; } }
                if (startHost != null) break;
            }
            if (startHost == null) return false;
            p.start = p.rooms.Count - 1;

            // uscita: una stanza dell'altra ala, la piu' lontana a piedi dall'arrivo; prima i locali di servizio o di
            // culto, le camere solo se non c'e' altro
            var dist = p.WalkDist(startHost.id);
            bool Preferred(Room q) => IsPreferredExit(q.kind);
            var order = new List<Room>();
            foreach (var q in p.rooms)
            {
                if (q.IsLift || q.wing == startHost.wing || q.kind == RoomKind.Corridoio || q.kind == RoomKind.Navata || q.kind == RoomKind.Navatella) continue;
                if (dist[q.id] == float.MaxValue) continue;
                order.Add(q);
            }
            order.Sort((a, b) =>
            {
                int pa = Preferred(a) ? 0 : 1, pb = Preferred(b) ? 0 : 1;
                return pa != pb ? pa.CompareTo(pb) : dist[b.id].CompareTo(dist[a.id]);
            });
            // un po' di casualita' tra le prime due
            if (order.Count > 2 && Preferred(order[0]) && Preferred(order[1]) && r.Chance(0.4)) { var t = order[0]; order[0] = order[1]; order[1] = t; }
            foreach (var q in order)
                if (AddLift(p, r, q)) { p.exit = p.rooms.Count - 1; return true; }
            return false;
        }

        // ------------------------------------------------------------------ finestre
        static void AddWindows(Plan p, Rng r)
        {
            int n = p.rooms.Count;
            for (int i = 0; i < n; i++)
            {
                var room = p.rooms[i];
                if (room.IsLift) continue;
                foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                {
                    bool ax = Plan.AlongX(s);
                    int line = Plan.Line(room, s);
                    foreach (var seg in p.Segments(room, s))
                    {
                        if (seg.other >= 0) continue;
                        int len = seg.a1 - seg.a0;
                        switch (room.kind)
                        {
                            case RoomKind.Camera:
                                if (len >= 30) Win(p, room, ax, line, (seg.a0 + seg.a1) / 2, 12, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Suite:
                                for (int c = seg.a0 + 18; c <= seg.a1 - 18; c += 30) Win(p, room, ax, line, c, 14, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Atrio:
                                for (int c = seg.a0 + 20; c <= seg.a1 - 20; c += 40) Win(p, room, ax, line, c, 16, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Corridoio:
                                if (len <= 40 && len >= 20) Win(p, room, ax, line, (seg.a0 + seg.a1) / 2, 12, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Ristorante:
                            case RoomKind.SalaBallo:
                                for (int c = seg.a0 + 22; c <= seg.a1 - 22; c += 44) Win(p, room, ax, line, c, 16, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Cucina:
                                if (len >= 30) Win(p, room, ax, line, (seg.a0 + seg.a1) / 2, 12, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Servizio:
                                if (len >= 20) Win(p, room, ax, line, (seg.a0 + seg.a1) / 2, 8, OpenStyle.Feritoia, r);
                                break;
                            case RoomKind.Nartece:
                                for (int c = seg.a0 + 18; c <= seg.a1 - 18; c += 36) Win(p, room, ax, line, c, 12, OpenStyle.Lancetta, r);
                                break;
                            case RoomKind.Navata:
                                for (int c = seg.a0 + 22; c <= seg.a1 - 22; c += 45) Win(p, room, ax, line, c, 12, OpenStyle.Lancetta, r);
                                break;
                            case RoomKind.Navatella:
                                for (int c = seg.a0 + 22; c <= seg.a1 - 22; c += 45) Win(p, room, ax, line, c, 12, OpenStyle.Lancetta, r);
                                break;
                            case RoomKind.Cappella:
                                if (len >= 30) Win(p, room, ax, line, (seg.a0 + seg.a1) / 2, 12, OpenStyle.Lancetta, r);
                                break;
                            case RoomKind.Presbiterio:
                                for (int c = seg.a0 + 16; c <= seg.a1 - 16; c += 32) { var o = Win(p, room, ax, line, c, 14, OpenStyle.Lancetta, r); if (o != null) { o.sill = 1.6f; o.top = 6.6f; } }
                                break;
                            case RoomKind.Sacrestia:
                                if (len >= 24) Win(p, room, ax, line, (seg.a0 + seg.a1) / 2, 10, OpenStyle.Lancetta, r);
                                break;
                            case RoomKind.Cripta:
                                for (int c = seg.a0 + 14; c <= seg.a1 - 14; c += 28) Win(p, room, ax, line, c, 8, OpenStyle.Feritoia, r);
                                break;
                            case RoomKind.Campanile:
                                if (len >= 24) { var o = Win(p, room, ax, line, (seg.a0 + seg.a1) / 2, 10, OpenStyle.Lancetta, r); if (o != null) { o.sill = 2.4f; o.top = 5.6f; } }
                                break;
                        }
                    }
                }
                // cleristorio: finestre alte nei muri lunghi della navata, sopra il tetto delle navate laterali
                if (room.kind == RoomKind.Navata)
                {
                    foreach (Side s in new[] { Side.S, Side.N })
                    {
                        foreach (var seg in p.Segments(room, s))
                        {
                            if (seg.other < 0 || p.rooms[seg.other].kind != RoomKind.Navatella) continue;
                            int line = Plan.Line(room, s);
                            for (int c = seg.a0 + 22; c <= seg.a1 - 22; c += 45)
                            {
                                var o = new Opening { a = room.id, b = -1, alongX = true, line = line, center = c, width = 10, style = OpenStyle.Lancetta, oneSided = true, variant = r.Range(0, 4) };
                                Opening.Defaults(o);
                                o.sill = 6.7f; o.top = 8.45f;
                                p.openings.Add(o);
                            }
                        }
                    }
                    // le due testate della navata: la parete sopra l'arco trionfale si lascia piena (rosone: v2)
                }
            }
        }

        static Opening Win(Plan p, Room room, bool alongX, int line, int center, int width, OpenStyle st, Rng r)
        {
            var o = new Opening { a = room.id, b = -1, alongX = alongX, line = line, center = center, width = width, style = st, variant = r.Range(0, 4) };
            Opening.Defaults(o);
            p.openings.Add(o);
            return o;
        }

        // ------------------------------------------------------------------ obiettivi
        /// <summary>Sceglie le stanze dei candelabri da accendere: lontane tra loro e dall'arrivo (campionamento del punto piu'
        /// lontano), con almeno una di chiesa e una d'albergo.</summary>
        static void PickObjectives(Plan p, Rng r, int floor)
        {
            int n = Math.Min(6, 2 + Math.Max(0, floor - 1) / 2 + (p.units - 1));
            var elig = new List<Room>();
            foreach (var q in p.rooms)
            {
                if (q.IsLift || q.kind == RoomKind.Corridoio || q.kind == RoomKind.Servizio || q.kind == RoomKind.Campanile) continue;
                if (q.W * q.D < 1100) continue;
                elig.Add(q);
            }
            if (elig.Count == 0) return;
            var start = p.WalkDist(p.start);
            var chosen = new List<Room>();
            var dists = new List<float[]>();
            for (int k = 0; k < n && elig.Count > 0; k++)
            {
                Room best = null; float bs = -1f;
                foreach (var q in elig)
                {
                    if (chosen.Contains(q) || start[q.id] == float.MaxValue) continue;
                    float m = start[q.id];
                    foreach (var d in dists) m = Math.Min(m, d[q.id]);
                    m *= 0.7f + (float)r.Value() * 0.6f;                 // un po' di casualita'
                    if (k == 0) m *= 0.6f + 0.4f * Math.Min(1f, start[q.id] / 40f);
                    if (m > bs) { bs = m; best = q; }
                }
                if (best == null) break;
                chosen.Add(best);
                dists.Add(p.WalkDist(best.id));
            }
            // almeno una per ala (se si puo')
            if (chosen.Count >= 2)
            {
                bool anyWing0 = chosen.Exists(q => q.wing == 0), anyWing1 = chosen.Exists(q => q.wing == 1);
                if (!anyWing0 || !anyWing1)
                {
                    int want = anyWing0 ? 1 : 0;
                    foreach (var q in elig)
                        if (!chosen.Contains(q) && q.wing == want) { chosen[chosen.Count - 1] = q; break; }
                }
            }
            foreach (var q in chosen) p.objectives.Add(q.id);
        }

        // ------------------------------------------------------------------ piano composto
        /// <summary>Quante ali-coppia compongono un piano: tre ai primi due piani, poi una in piu' ogni due, al massimo cinque.
        /// Nei tentativi di ripiego (simple) sono due, cosi' il piano riesce quasi sempre.</summary>
        public static int UnitsFor(int floor, bool simple) => simple ? 2 : Math.Min(5, 3 + Math.Max(0, floor - 1) / 2);

        /// <summary>Un piano e' fatto di piu' ali-coppia: ognuna e' il piano di prima (un'ala d'albergo e una di chiesa, unite da un
        /// portale). Si attaccano una all'altra con una porta, su un lato qualunque e con una rotazione a caso.</summary>
        static Plan Compound(Rng r, bool simple, int units, Theme theme)
        {
            var p = OneUnit(r, simple, theme);
            if (p == null) return null;
            SetUnit(p, 0);
            for (int u = 1; u < units; u++)
            {
                var next = OneUnit(r, simple, theme);
                if (next == null) return null;
                SetUnit(next, u);
                if (!Attach(p, next, r)) return null;
            }
            p.units = units;
            return p;
        }

        static void SetUnit(Plan p, int unit)
        {
            foreach (var room in p.rooms) room.unit = unit;
        }

        /// <summary>Un'ala-coppia del tema: la prima ala e la seconda, unite dal portale proprio del tema.</summary>
        static Plan OneUnit(Rng r, bool simple, Theme theme)
        {
            switch (theme)
            {
                case Theme.Teatro: return Assemble(TheatreWing(r, simple), StageWing(r, simple), r, simple, Joint.Teatro());
                case Theme.Sanatorio: return Assemble(HotelWing(r, simple, KindsIstituto), ChurchWing(r, simple), r, simple, Joint.Istituto());
                default: return Assemble(HotelWing(r, simple, KindsAlbergo), ChurchWing(r, simple), r, simple, Joint.Albergo());
            }
        }

        /// <summary>Il bordo piu' esterno di un insieme di stanze sul lato 's' (z minima per S, z massima per N, x minima per W...).</summary>
        static int Extreme(List<Room> rs, Side s)
        {
            bool min = s == Side.S || s == Side.W;
            int v = min ? int.MaxValue : int.MinValue;
            foreach (var q in rs)
            {
                int c = s == Side.S ? q.z0 : s == Side.N ? q.z1 : s == Side.W ? q.x0 : q.x1;
                v = min ? Math.Min(v, c) : Math.Max(v, c);
            }
            return v;
        }

        /// <summary>Stanze dove si puo' aprire una porta tra ali. Presbiterio e cappella no: la pala e l'altare vi sono messi contro
        /// il muro di fondo senza controllare le porte (RoomKitsChiesa, check=false) e una porta li chiuderebbe fuori dalla navmesh.</summary>
        static bool AttachOk(Room q) => q.kind != RoomKind.Presbiterio && q.kind != RoomKind.Cappella;

        /// <summary>Appoggia l'ala 'u' al lato 's' di 'p', fuori dal perimetro di 'p', e la collega con una porta in un tratto di
        /// muro in comune. Le ali si toccano direttamente (niente corridoi in mezzo), come le camere d'albergo tra loro.</summary>
        static bool Attach(Plan p, Plan u, Rng r)
        {
            p.ComputeBounds();
            const int Share = 22;                                  // dm di muro in comune: porta da 14 e 4 di margine per lato
            for (int tries = 0; tries < 60; tries++)
            {
                Side s = AllSides[r.Range(0, 4)], os = Plan.Opposite(s);
                var xf = new Xf { mirror = r.Chance(0.5), rot = r.Range(0, 4) };
                var rr = new List<Room>(u.rooms.Count);
                foreach (var room in u.rooms) rr.Add(Transform(room, xf));
                int target = Extreme(p.rooms, s), ext = Extreme(rr, os);
                var pa = p.rooms.FindAll(q => AttachOk(q) && Plan.Line(q, s) == target);
                var pb = rr.FindAll(q => AttachOk(q) && Plan.Line(q, os) == ext);
                if (pa.Count == 0 || pb.Count == 0) continue;
                Room A = pa[r.Range(0, pa.Count)], B = pb[r.Range(0, pb.Count)];
                Plan.Span(A, s, out int alo, out int ahi);
                Plan.Span(B, os, out int blo, out int bhi);
                if (ahi - alo < Share || bhi - blo < Share) continue;
                // spostamento lungo il muro: la sovrapposizione dei due tratti resta di almeno Share (intervallo esatto)
                int minAlong = alo + Share - bhi, maxAlong = ahi - Share - blo;
                if (minAlong > maxAlong) continue;
                int along = r.Range(minAlong, maxAlong + 1);
                int lo = Math.Max(alo, blo + along), hi = Math.Min(ahi, bhi + along);
                // spostamento: normale al muro (target - ext) e lungo il muro (along)
                bool alongX = Plan.AlongX(s);
                xf.tx = alongX ? along : target - ext;
                xf.tz = alongX ? target - ext : along;
                var tr = new List<Room>(rr.Count);
                foreach (var room in u.rooms) tr.Add(Transform(room, xf));
                int bx0 = p.minX, bz0 = p.minZ, bx1 = p.maxX, bz1 = p.maxZ;
                bool clash = false;
                foreach (var t in tr)
                {
                    bx0 = Math.Min(bx0, t.x0); bz0 = Math.Min(bz0, t.z0); bx1 = Math.Max(bx1, t.x1); bz1 = Math.Max(bz1, t.z1);
                    foreach (var q in p.rooms) if (Overlap(q, t, 0)) clash = true;
                }
                if (clash || bx1 - bx0 > MaxSpan || bz1 - bz0 > MaxSpan) continue;
                int off = p.rooms.Count;
                foreach (var t in tr) { t.id = p.rooms.Count; p.rooms.Add(t); }
                foreach (var o in u.openings)
                {
                    var t = TransformOpening(o, xf);
                    t.a += off;
                    if (t.b >= 0) t.b += off;
                    p.openings.Add(t);
                }
                AddDoor(p, A, p.rooms[off + B.id], s, lo, hi);
                p.ComputeBounds();
                return true;
            }
            return false;
        }

        /// <summary>Porta (arcata se una delle due ali e' di chiesa) tra due stanze che si toccano sul lato 's' di 'a'.</summary>
        static void AddDoor(Plan p, Room a, Room b, Side s, int lo, int hi)
        {
            bool arch = a.church || b.church;
            var o = new Opening
            {
                a = a.id, b = b.id, alongX = Plan.AlongX(s), line = Plan.Line(a, s), center = (lo + hi) / 2,
                width = arch ? 16 : 14, style = arch ? OpenStyle.Arco : OpenStyle.PortaLarga,
            };
            Opening.Defaults(o);
            p.openings.Add(o);
        }

        // ------------------------------------------------------------------ rifinitura e controlli
        static readonly string[] HotelNames = { "Corridoio dei Penitenti", "Ala del Silenzio", "Piano delle Candele", "Il Corridoio che Ascolta", "Ala dei Pellegrini", "Piano degli Ospiti Eterni" };
        static readonly string[] ChurchNames = { "La Navata Occupata", "Il Coro Spento", "Cappelle del Piano Basso", "La Chiesa sotto l'Hotel", "La Navata dei Mille Occhi", "L'Altare di Servizio" };

        static Plan Build(Rng r, int seed, int floor, bool simple)
        {
            // il tema dipende da seme e piano, non dal tentativo: cosi' ogni tema esce con la stessa probabilita'
            var theme = PickTheme(floor, new Rng(unchecked(seed * 73 + floor * 1009 + 5)));
            var p = Compound(r, simple, UnitsFor(floor, simple), theme);
            if (p == null) return null;
            p.theme = theme;
            p.seed = seed; p.floor = floor;
            if (!PlaceLifts(p, r)) return null;
            AddWindows(p, r);
            AddThemeWindows(p, r);
            PickObjectives(p, r, floor);
            p.ComputeBounds();
            foreach (var room in p.rooms) { room.label = room.kind.ToString(); room.variant = r.Range(0, 1000); }
            p.epithet = Epithet(p, r);
            p.name = "Piano " + floor;
            return p;
        }

        /// <summary>Controlli: niente sovrapposizioni, tutte le stanze raggiungibili dall'arrivo, aperture dentro i muri e
        /// senza accavallarsi, dimensioni ragionevoli.</summary>
        public static bool Validate(Plan p, out string why)
        {
            why = "";
            for (int i = 0; i < p.rooms.Count; i++)
                for (int j = i + 1; j < p.rooms.Count; j++)
                    if (Overlap(p.rooms[i], p.rooms[j], 0)) { why = $"sovrapposte {p.rooms[i]} {p.rooms[j]}"; return false; }
            if (p.start < 0 || p.exit < 0 || p.start == p.exit) { why = "ascensori mancanti"; return false; }
            var hops = p.Hops(p.start);
            foreach (var r in p.rooms) if (hops[r.id] < 0) { why = "irraggiungibile " + r; return false; }
            foreach (var o in p.openings)
            {
                var a = p.rooms[o.a];
                Side s = o.alongX ? (Plan.Line(a, Side.N) == o.line ? Side.N : Side.S) : (Plan.Line(a, Side.E) == o.line ? Side.E : Side.W);
                if (Plan.Line(a, s) != o.line) { why = "apertura fuori linea"; return false; }
                Plan.Span(a, s, out int lo, out int hi);
                if (o.Lo < lo || o.Hi > hi) { why = "apertura fuori muro"; return false; }
                if (o.b >= 0)
                {
                    var b = p.rooms[o.b];
                    Side os = Plan.Opposite(s);
                    if (Plan.Line(b, os) != o.line) { why = "apertura senza vicino"; return false; }
                    Plan.Span(b, os, out int blo, out int bhi);
                    if (o.Lo < blo || o.Hi > bhi) { why = "apertura fuori dal vicino"; return false; }
                }
                else if (!o.oneSided)
                {
                    // finestra: deve dare sul vuoto
                    bool voidOk = false;
                    foreach (var seg in p.Segments(a, s)) if (seg.other < 0 && o.Lo >= seg.a0 && o.Hi <= seg.a1) voidOk = true;
                    if (!voidOk) { why = "finestra non sul vuoto " + a; return false; }
                }
            }
            // aperture sullo stesso muro che si toccano
            for (int i = 0; i < p.openings.Count; i++)
                for (int j = i + 1; j < p.openings.Count; j++)
                {
                    var o = p.openings[i]; var q = p.openings[j];
                    if (o.alongX != q.alongX || o.line != q.line) continue;
                    bool shareRoom = o.a == q.a || o.a == q.b || (o.b >= 0 && (o.b == q.a || o.b == q.b));
                    if (!shareRoom) continue;
                    // finestre alte e porte non si toccano: contano solo aperture con fasce di altezza che si sovrappongono
                    if (o.sill >= q.top || q.sill >= o.top) continue;
                    if (o.Lo < q.Hi + 1 && q.Lo < o.Hi + 1) { why = $"aperture accavallate {p.rooms[o.a]}"; return false; }
                }
            p.ComputeBounds();
            if (p.maxX - p.minX > MaxSpan || p.maxZ - p.minZ > MaxSpan) { why = "troppo grande"; return false; }
            return true;
        }
    }
}
