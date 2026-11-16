using System;
using System.Collections.Generic;

namespace PryingEyes.Proc
{
    // Gli annessi sono le stanze di difficile accesso dei piani a pianta a griglia: una torre con una cella in alto (scala a chiocciola,
    // nascosta in una parete finta oppure no), una cripta sotto il pavimento (si scende da una botola) e un giardino segreto all'aperto,
    // fuori dai muri del piano. Come la pianta, sono pura geometria intera (decimetri) e si generano e si provano fuori da Unity
    // (tools/planlab). Il costruttore runtime e' FloorBuilder.Annex.cs; la sorte dei cancelli (leve, tributo, chiave) e' AnnexRuntime.cs.

    public enum AnnexKind { Torre, Cripta, Giardino }

    /// <summary>Come si apre un annesso: Sigilli (leve da tirare in giro per la sala madre, in alto e in basso), Tributo (un'ara che chiede
    /// molti punti) o Chiave (la custodisce un altro annesso dello stesso piano).</summary>
    public enum GateKind { Sigilli, Tributo, Chiave }

    public sealed class Annex
    {
        public int id, host, seed;
        public AnnexKind kind;
        public GateKind gate;
        public bool secret;                 // torre: la porta e' una parete finta (nessuna cornice) finche' non si apre
        public int levers, price;
        public int keyHolder = -1;          // Chiave: l'annesso che la custodisce
        public int cx, cz;                  // asse della torre o della botola (dm)
        public int yaw;                     // verso cui guarda la porta (torre) o la camera (cripta): 0 +z, 90 +x, 180 -z, 270 -x
        public int floorY;                  // quota del pavimento della stanza dell'annesso, dm (cripta: negativa)
        public int x0, z0, x1, z1;          // la stanza dell'annesso: cella in alto, camera della cripta, giardino (dm)
        public Side side;                   // giardino: il lato del muro dove si apre la porta
        public int line, center, width;     // giardino: la porta nel muro della sala madre (dm)
        public int px0, pz0, px1, pz1;      // giardino: il passaggio fra il muro e il giardino (dm)

        public bool HasShaft => kind != AnnexKind.Giardino;

        public float CenterX => (x0 + x1) * 0.05f;
        public float CenterZ => (z0 + z1) * 0.05f;
        public float AxisX => cx * 0.1f;
        public float AxisZ => cz * 0.1f;
        public float FloorY => floorY * 0.1f;

        /// <summary>Il punto (m) e' dentro il giardino o il suo passaggio: lo spazio fuori dai muri che appartiene alla sala madre.</summary>
        public bool OutsideContains(float x, float z)
        {
            if (kind != AnnexKind.Giardino) return false;
            return In(x, z, x0, z0, x1, z1) || In(x, z, px0, pz0, px1, pz1);
        }

        static bool In(float x, float z, int a0, int b0, int a1, int b1) => x >= a0 * 0.1f && x <= a1 * 0.1f && z >= b0 * 0.1f && z <= b1 * 0.1f;

        public override string ToString() => $"{id}:{kind}@{host} gate={gate}{(secret ? " secret" : "")}";
    }

    public static class PlanAnnexGen
    {
        /// <summary>Raggio esterno (dm) del pozzo della botola e del fusto della torre: muro di 30 cm attorno a 2,4 m di raggio utile (la scala a chiocciola gira tra 0,4 e 2,3 m dall'asse).</summary>
        public const int ShaftOuter = 27, ShaftInner = 24;
        public const int CellHalf = 48;                         // la cella in cima alla torre: 9,6 x 9,6 m
        public const int CryptLength = 100, CryptWidth = 80;    // la camera della cripta: da 3,4 m dietro l'asse a 10 m davanti, larga 8 m
        public const int GardenW = 150, GardenD = 130;          // il giardino: 15 x 13 m
        public const int PassageLen = 40, PassageW = 24, DoorW = 18;
        /// <summary>Quanto sotto il pavimento sta la cripta (dm), e la quota di una cella su tre piani di balconate o su uno (dm).</summary>
        public const int CryptDepth = 44, CellHigh = 108, CellLow = 72;
        /// <summary>Altezza interna della cella e della camera (m).</summary>
        public const float CellHeight = 3.4f, CryptHeight = 3.3f;

        /// <summary>Costruisce gli annessi di un piano a griglia, in modo deterministico dal seme del piano.</summary>
        public static void Generate(Plan plan, Rng rng)
        {
            plan.annexes.Clear();
            plan.annexDoors.Clear();
            if (!plan.grid || plan.rooms.Count == 0) return;
            var kinds = new List<AnnexKind>();
            if (plan.floor <= 1) { kinds.Add(AnnexKind.Cripta); kinds.Add(AnnexKind.Torre); }
            else if (plan.floor == 2) { kinds.Add(AnnexKind.Torre); kinds.Add(AnnexKind.Giardino); }
            else { kinds.Add(AnnexKind.Torre); kinds.Add(AnnexKind.Cripta); kinds.Add(AnnexKind.Giardino); }
            var used = new HashSet<int>();
            foreach (var kind in kinds)
            {
                Annex a = null;
                switch (kind)
                {
                    case AnnexKind.Torre: a = TryTower(plan, rng, used); break;
                    case AnnexKind.Cripta: a = TryCrypt(plan, rng, used); break;
                    default: a = TryGarden(plan, rng, used); break;
                }
                if (a == null) continue;
                a.id = plan.annexes.Count;
                a.seed = rng.Range(1, 1 << 30);
                used.Add(a.host);
                plan.annexes.Add(a);
                if (a.kind == AnnexKind.Giardino)
                    plan.annexDoors.Add(MakeGardenDoor(plan.rooms[a.host], a));
            }
            AssignGates(plan, rng);
            plan.ComputeBounds();
        }

        // ------------------------------------------------------------------ cancelli
        static void AssignGates(Plan plan, Rng rng)
        {
            int floor = Math.Max(1, plan.floor);
            foreach (var a in plan.annexes)
            {
                bool levers = rng.Chance(0.62);
                a.gate = levers ? GateKind.Sigilli : GateKind.Tributo;
                a.levers = levers ? (floor <= 3 ? 2 : 3) : 0;
                a.price = levers ? 0 : 1500 + 250 * Math.Min(floor, 8);
            }
            // il giardino si apre con la chiave di un altro annesso, quando ce n'e' uno (il piu' difficile: la torre)
            var garden = plan.annexes.Find(x => x.kind == AnnexKind.Giardino);
            if (garden == null) return;
            var holder = plan.annexes.Find(x => x.kind == AnnexKind.Torre) ?? plan.annexes.Find(x => x.kind == AnnexKind.Cripta);
            if (holder == null) return;
            garden.gate = GateKind.Chiave; garden.levers = 0; garden.price = 0; garden.keyHolder = holder.id;
        }

        // ------------------------------------------------------------------ zone libere per un'opera tonda
        /// <summary>L'intervallo in cui puo' cadere il centro di un'opera tonda di raggio ShaftOuter senza toccare muri, balconate e le scale
        /// dell'atrio (le quote e i passi sono quelli di FloorBuilder.Galleries/Atrium).</summary>
        static bool Zone(Room r, bool tower, out int xa, out int xb, out int za, out int zb)
        {
            const int gap = 15;
            if (r.upperGallery && r.GalleryLevels == 2)
            {
                xa = r.x0 + 44 + gap + ShaftOuter; xb = r.x1 - 36 - gap - ShaftOuter;
                za = r.z0 + 70 + gap + ShaftOuter; zb = r.z1 - 70 - gap - ShaftOuter;
            }
            else if (r.upperGallery)
            {
                xa = r.x0 + 60; xb = r.x1 - 44 - gap - ShaftOuter;
                za = r.z0 + 44 + gap + ShaftOuter; zb = r.z1 - 44 - gap - ShaftOuter;
            }
            else
            {
                int m = 40 + ShaftOuter;
                xa = r.x0 + m; xb = r.x1 - m; za = r.z0 + m; zb = r.z1 - m;
            }
            return xa < xb && za < zb;
        }

        static bool Eligible(Room r, HashSet<int> used) =>
            !r.IsLift && r.type != RoomType.Segreta && r.type != RoomType.Partenza && r.cornerRadius <= 0f && !used.Contains(r.id) && !r.Open;

        static void Shuffle<T>(List<T> l, Rng rng)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = rng.Range(0, i + 1); T t = l[i]; l[i] = l[j]; l[j] = t; }
        }

        static bool NearOpening(Plan plan, Room r, int x, int z, int clear)
        {
            foreach (var o in plan.openings)
            {
                if (o.a != r.id && o.b != r.id) continue;
                if (o.b < 0 && o.sill > 0.5f) continue;     // finestre alte: nessun vincolo
                int ox = o.alongX ? o.center : o.line, oz = o.alongX ? o.line : o.center;
                long dx = ox - x, dz = oz - z;
                if (dx * dx + dz * dz < (long)clear * clear) return true;
            }
            return false;
        }

        /// <summary>La direzione (0 +z, 90 +x, 180 -z, 270 -x) verso la parete piu' lontana dall'asse: li' si apre la porta o la camera.</summary>
        static int FarthestYaw(Room r, int x, int z)
        {
            int[] d = { r.z1 - z, r.x1 - x, z - r.z0, x - r.x0 };
            int best = 0;
            for (int i = 1; i < 4; i++) if (d[i] > d[best]) best = i;
            return best * 90;
        }

        // ------------------------------------------------------------------ torre
        static Annex TryTower(Plan plan, Rng rng, HashSet<int> used)
        {
            var cands = new List<Room>();
            foreach (var r in plan.rooms)
            {
                if (!Eligible(r, used) || !r.upperGallery || r.type == RoomType.Boss) continue;
                if (r.GalleryLevels == 2 ? (r.W < 240 || r.D < 240) : (r.W < 180 || r.D < 180)) continue;
                if (Zone(r, true, out _, out _, out _, out _)) cands.Add(r);
            }
            if (cands.Count == 0) return null;
            Shuffle(cands, rng);
            foreach (var r in cands)
            {
                Zone(r, true, out int xa, out int xb, out int za, out int zb);
                int floorY = r.GalleryLevels == 2 ? CellHigh : CellLow;
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    // fuori dal centro (li' sta il lampadario e si incrociano i passaggi): da 3,5 a 7 m lungo l'asse lungo
                    float sx = r.W >= r.D ? (rng.Chance(0.5) ? -1f : 1f) * rng.Range(35f, 70f) : rng.Range(-15f, 15f);
                    float sz = r.W >= r.D ? rng.Range(-15f, 15f) : (rng.Chance(0.5) ? -1f : 1f) * rng.Range(35f, 70f);
                    int cx = Clamp((int)Math.Round((r.x0 + r.x1) * 0.5f + sx), xa, xb), cz = Clamp((int)Math.Round((r.z0 + r.z1) * 0.5f + sz), za, zb);
                    if (NearOpening(plan, r, cx, cz, 75)) continue;
                    int half = Math.Min(CellHalf, Math.Min(Math.Min(cx - r.x0, r.x1 - cx), Math.Min(cz - r.z0, r.z1 - cz)) - 12);
                    if (half < 40) continue;
                    return new Annex
                    {
                        kind = AnnexKind.Torre, host = r.id, cx = cx, cz = cz, yaw = FarthestYaw(r, cx, cz), floorY = floorY,
                        x0 = cx - half, z0 = cz - half, x1 = cx + half, z1 = cz + half, secret = rng.Chance(0.5)
                    };
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ cripta
        static Annex TryCrypt(Plan plan, Rng rng, HashSet<int> used)
        {
            var cands = new List<Room>();
            foreach (var r in plan.rooms)
            {
                if (!Eligible(r, used) || Math.Min(r.W, r.D) < 150) continue;
                if (Zone(r, false, out _, out _, out _, out _)) cands.Add(r);
            }
            if (cands.Count == 0) return null;
            Shuffle(cands, rng);
            foreach (var r in cands)
            {
                Zone(r, false, out int xa, out int xb, out int za, out int zb);
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    int cx = rng.Range(xa, xb + 1), cz = rng.Range(za, zb + 1);
                    if (NearOpening(plan, r, cx, cz, 60)) continue;
                    // nei centri delle sale a balconata il lampadario e le colonne: meglio altrove
                    if (r.upperGallery && Math.Abs(cx - (r.x0 + r.x1) / 2) < 30 && Math.Abs(cz - (r.z0 + r.z1) / 2) < 30) continue;
                    int yaw = FarthestYaw(r, cx, cz);
                    ChamberRect(cx, cz, yaw, out int x0, out int z0, out int x1, out int z1);
                    bool clash = false;
                    foreach (var other in plan.annexes)
                        if (other.kind == AnnexKind.Cripta && Overlap(x0, z0, x1, z1, other.x0, other.z0, other.x1, other.z1, 20)) clash = true;
                    if (clash) continue;
                    return new Annex { kind = AnnexKind.Cripta, host = r.id, cx = cx, cz = cz, yaw = yaw, floorY = -CryptDepth, x0 = x0, z0 = z0, x1 = x1, z1 = z1 };
                }
            }
            return null;
        }

        /// <summary>La camera della cripta: comincia 3,4 m dietro l'asse del pozzo (la scala sta in un capo della sala) e si allunga fino a
        /// CryptLength davanti, verso 'yaw'.</summary>
        public static void ChamberRect(int cx, int cz, int yaw, out int x0, out int z0, out int x1, out int z1)
        {
            int s = yaw == 90 ? 1 : yaw == 270 ? -1 : 0, t = yaw == 0 ? 1 : yaw == 180 ? -1 : 0;
            int a = -34, b = CryptLength;
            if (s != 0) { x0 = Math.Min(cx + s * a, cx + s * b); x1 = Math.Max(cx + s * a, cx + s * b); z0 = cz - CryptWidth / 2; z1 = cz + CryptWidth / 2; }
            else { z0 = Math.Min(cz + t * a, cz + t * b); z1 = Math.Max(cz + t * a, cz + t * b); x0 = cx - CryptWidth / 2; x1 = cx + CryptWidth / 2; }
        }

        // ------------------------------------------------------------------ giardino
        static Annex TryGarden(Plan plan, Rng rng, HashSet<int> used)
        {
            var found = new List<Annex>();
            foreach (var r in plan.rooms)
            {
                if (!Eligible(r, used) || Math.Max(r.W, r.D) < 120) continue;
                foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                {
                    bool ax = Plan.AlongX(s);
                    int line = Plan.Line(r, s);
                    foreach (var seg in plan.Segments(r, s))
                    {
                        if (seg.other >= 0 || seg.a1 - seg.a0 < 80) continue;
                        for (int c = seg.a0 + 36; c <= seg.a1 - 36; c += 12)
                        {
                            bool clear = true;
                            foreach (var o in plan.openings)
                                if (o.alongX == ax && o.line == line && c + DoorW / 2 + 14 > o.Lo && c - DoorW / 2 - 14 < o.Hi) { clear = false; break; }
                            foreach (var o in plan.annexDoors)
                                if (o.alongX == ax && o.line == line && Math.Abs(c - o.center) < 60) { clear = false; break; }
                            if (!clear) continue;
                            var a = GardenAt(plan, r, s, c, 0);
                            if (a == null && (a = GardenAt(plan, r, s, c, -30)) == null && (a = GardenAt(plan, r, s, c, 30)) == null) continue;
                            found.Add(a);
                        }
                    }
                }
            }
            if (found.Count == 0) return null;
            return found[rng.Range(0, found.Count)];
        }

        static Annex GardenAt(Plan plan, Room r, Side s, int center, int shift)
        {
            bool ax = Plan.AlongX(s);
            int line = Plan.Line(r, s);
            int dir = s == Side.N || s == Side.E ? 1 : -1;          // verso l'esterno
            // il passaggio parte dalla linea del muro; il giardino comincia dopo un secondo muro di 30 cm
            int pa = line, pb = line + dir * PassageLen;
            int ga = pb + dir * Plan.T, gb = ga + dir * GardenD;
            int gl = center + shift - GardenW / 2, gh = gl + GardenW;
            int cl = center - PassageW / 2, ch = center + PassageW / 2;
            int px0, pz0, px1, pz1, x0, z0, x1, z1;
            if (ax) { px0 = cl; px1 = ch; pz0 = Math.Min(pa, pb); pz1 = Math.Max(pa, pb); x0 = gl; x1 = gh; z0 = Math.Min(ga, gb); z1 = Math.Max(ga, gb); }
            else { pz0 = cl; pz1 = ch; px0 = Math.Min(pa, pb); px1 = Math.Max(pa, pb); z0 = gl; z1 = gh; x0 = Math.Min(ga, gb); x1 = Math.Max(ga, gb); }
            foreach (var other in plan.rooms)
            {
                if (Overlap(x0, z0, x1, z1, other.x0, other.z0, other.x1, other.z1, 8)) return null;
                if (other != r && Overlap(px0, pz0, px1, pz1, other.x0, other.z0, other.x1, other.z1, 8)) return null;
            }
            foreach (var other in plan.annexes)
            {
                if (other.kind != AnnexKind.Giardino) continue;
                if (Overlap(x0, z0, x1, z1, other.x0, other.z0, other.x1, other.z1, 12) || Overlap(x0, z0, x1, z1, other.px0, other.pz0, other.px1, other.pz1, 12)) return null;
            }
            return new Annex
            {
                kind = AnnexKind.Giardino, host = r.id, side = s, line = line, center = center, width = DoorW, floorY = 0,
                px0 = px0, pz0 = pz0, px1 = px1, pz1 = pz1, x0 = x0, z0 = z0, x1 = x1, z1 = z1,
                yaw = s == Side.N ? 0 : s == Side.E ? 90 : s == Side.S ? 180 : 270, secret = true
            };
        }

        static Opening MakeGardenDoor(Room host, Annex a)
        {
            var o = new Opening { a = host.id, b = -2, alongX = Plan.AlongX(a.side), line = a.line, center = a.center, width = a.width, style = OpenStyle.Porta, variant = a.id };
            Opening.Defaults(o);
            return o;
        }

        // ------------------------------------------------------------------ geometria
        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>I due rettangoli [a0,a1) x [b0,b1) si toccano o si sovrappongono, con un margine (dm).</summary>
        public static bool Overlap(int ax0, int az0, int ax1, int az1, int bx0, int bz0, int bx1, int bz1, int margin) =>
            ax0 < bx1 + margin && ax1 > bx0 - margin && az0 < bz1 + margin && az1 > bz0 - margin;

        // ------------------------------------------------------------------ controlli (per planlab e le prove)
        /// <summary>Controlla gli annessi di un piano: dentro la sala madre, lontani dai muri, senza toccare altre stanze ne' altri annessi.</summary>
        public static bool Validate(Plan plan, out string why)
        {
            why = "";
            var seen = new HashSet<int>();
            foreach (var a in plan.annexes)
            {
                if (a.host < 0 || a.host >= plan.rooms.Count) { why = "annesso senza sala madre " + a; return false; }
                if (!seen.Add(a.host)) { why = "due annessi nella stessa sala " + a; return false; }
                var r = plan.rooms[a.host];
                if (r.IsLift || r.type == RoomType.Segreta || r.type == RoomType.Partenza) { why = "sala madre non adatta " + a + " " + r; return false; }
                switch (a.kind)
                {
                    case AnnexKind.Torre:
                        if (!r.upperGallery) { why = "torre senza balconata " + a; return false; }
                        if (!Zone(r, true, out int xa, out int xb, out int za, out int zb) || a.cx < xa || a.cx > xb || a.cz < za || a.cz > zb) { why = "torre fuori zona " + a; return false; }
                        if (a.x0 < r.x0 + 10 || a.x1 > r.x1 - 10 || a.z0 < r.z0 + 10 || a.z1 > r.z1 - 10) { why = "cella fuori dalla sala " + a; return false; }
                        if (a.floorY / 10f + CellHeight + 2.5f > r.wallH) { why = $"sala troppo bassa per la cella {a} (alta {r.wallH})"; return false; }
                        break;
                    case AnnexKind.Cripta:
                        if (!Zone(r, false, out xa, out xb, out za, out zb) || a.cx < xa || a.cx > xb || a.cz < za || a.cz > zb) { why = "botola fuori zona " + a; return false; }
                        foreach (var o in plan.annexes)
                            if (o != a && o.kind == AnnexKind.Cripta && Overlap(a.x0, a.z0, a.x1, a.z1, o.x0, o.z0, o.x1, o.z1, 10)) { why = "due cripte si toccano"; return false; }
                        break;
                    default:
                        foreach (var other in plan.rooms)
                        {
                            if (Overlap(a.x0, a.z0, a.x1, a.z1, other.x0, other.z0, other.x1, other.z1, 6)) { why = "giardino dentro una stanza " + a + " " + other; return false; }
                            if (other.id != a.host && Overlap(a.px0, a.pz0, a.px1, a.pz1, other.x0, other.z0, other.x1, other.z1, 6)) { why = "passaggio del giardino dentro una stanza " + a + " " + other; return false; }
                        }
                        foreach (var o in plan.annexes)
                            if (o != a && o.kind == AnnexKind.Giardino && Overlap(a.x0, a.z0, a.x1, a.z1, o.x0, o.z0, o.x1, o.z1, 10)) { why = "due giardini si toccano"; return false; }
                        foreach (var o in plan.openings)
                            if (o.alongX == Plan.AlongX(a.side) && o.line == a.line && a.center + a.width / 2 + 10 > o.Lo && a.center - a.width / 2 - 10 < o.Hi) { why = "porta del giardino su un'apertura " + a; return false; }
                        break;
                }
                if (a.gate == GateKind.Sigilli && a.levers < 2) { why = "sigilli senza leve " + a; return false; }
                if (a.gate == GateKind.Tributo && a.price <= 0) { why = "tributo gratis " + a; return false; }
                if (a.gate == GateKind.Chiave && (a.keyHolder < 0 || a.keyHolder >= plan.annexes.Count || a.keyHolder == a.id)) { why = "chiave senza custode " + a; return false; }
            }
            return true;
        }
    }
}
