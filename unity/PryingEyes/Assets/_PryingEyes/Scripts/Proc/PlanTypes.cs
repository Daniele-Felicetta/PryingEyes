using System;
using System.Collections.Generic;

namespace PryingEyes.Proc
{
    // La pianta di un piano e' pura geometria intera (decimetri), senza UnityEngine: si genera e si valida anche fuori
    // da Unity (tools/planlab). Il costruttore runtime (FloorBuilder) la trasforma in muri, volte e arredi.

    /// <summary>Lato di una stanza: S = z minima, N = z massima, W = x minima, E = x massima.</summary>
    public enum Side { S = 0, N = 1, W = 2, E = 3 }

    /// <summary>Il tema di un piano: l'albergo con la chiesa (il tema di partenza), il teatro (sala e palcoscenico) o il sanatorio
    /// (istituto e cappella). Tutte le ali di un piano hanno lo stesso tema.</summary>
    public enum Theme { Albergo, Teatro, Sanatorio }

    public enum RoomKind
    {
        // albergo
        Atrio, Corridoio, Camera, Suite, Ristorante, Cucina, Servizio, SalaBallo,
        // chiesa
        Nartece, Navata, Navatella, Cappella, Presbiterio, Sacrestia, Cripta, Campanile,
        // speciale: la cabina di un ascensore (arrivo o uscita)
        Ascensore,
        // teatro: la sala e il palcoscenico
        Platea, Palco, Foyer, Palcoscenico, Camerino, Deposito, Ridotto,
        // sanatorio: l'istituto (la cappella e' la chiesa)
        Accettazione, Degenza, Corsia, Cella, Refettorio, SalaOperatoria, Lavanderia, Archivio, Obitorio, SalaMedici,
        Rotonda, Galleria, GiardinoInverno
    }

    /// <summary>Il ruolo di una stanza nella pianta a griglia (PlanGrid.cs), come i tipi di stanza di The Binding of Isaac: Partenza
    /// (l'ascensore di arrivo), Guardia (pattuglie), Candela (l'arena di un round), Negozio (armi a muro e scrigno), Biblioteca (i libri dei
    /// perk), Segreta (si entra solo da un passaggio basso: l'altare della consacrazione) e Boss (a guardia dell'uscita). Nelle piante
    /// di prima ogni stanza e' Normale.</summary>
    public enum RoomType { Normale, Partenza, Guardia, Candela, Negozio, Biblioteca, Segreta, Boss }

    public enum CeilKind { Flat, Beams, Gable, BarrelRound, BarrelPointed }

    public enum OpenStyle { Porta, PortaLarga, Arco, Arcata, Finestra, Lancetta, Feritoia, Ascensore, Passaggio }

    public sealed class Room
    {
        public int id;
        public RoomKind kind;
        public RoomType type;               // ruolo nella pianta a griglia (Normale nelle piante di prima)
        public bool church;                 // ala della chiesa (le altre sono albergo)
        public int x0, z0, x1, z1;          // decimetri, [x0,x1) x [z0,z1)
        public float wallH = 3f, ridgeH = 3f;   // metri: altezza dei muri e del punto piu' alto del soffitto
        public CeilKind ceil = CeilKind.Flat;
        public bool ridgeAlongX = true;     // asse di colmo/volta (solo Gable e Barrel)
        public int seed;                    // per gli arredi: ogni stanza ne ha uno suo
        public string label = "";
        public int variant;
        public bool upperGallery;           // doppia altezza e percorso superiore con due scale indipendenti
        public int GalleryLevels => upperGallery ? (W >= 240 && D >= 240 ? 2 : 1) : 0;
        public float cornerRadius;          // metri: raggio della pianta interna arrotondata, zero = rettangolare
        public int wing;                    // ala del piano: 0 la principale (albergo, sala, istituto), 1 la secondaria (chiesa, palcoscenico)
        public int unit;                    // ala-coppia di appartenenza (0 per la prima): vedi PlanGen.Compound

        public int W => x1 - x0;
        public int D => z1 - z0;
        public float CenterX => (x0 + x1) * 0.05f;   // metri
        public float CenterZ => (z0 + z1) * 0.05f;
        public bool IsLift => kind == RoomKind.Ascensore;

        public bool Contains(float x, float z, float margin = 0f)
        {
            float left = x0 * 0.1f + margin, right = x1 * 0.1f - margin;
            float bottom = z0 * 0.1f + margin, top = z1 * 0.1f - margin;
            if (x < left || x > right || z < bottom || z > top) return false;
            float radius = Math.Max(0f, cornerRadius - margin);
            if (radius == 0f) return true;
            float cx = Math.Max(left + radius, Math.Min(right - radius, x));
            float cz = Math.Max(bottom + radius, Math.Min(top - radius, z));
            return (x - cx) * (x - cx) + (z - cz) * (z - cz) <= radius * radius;
        }

        public override string ToString() => $"{id}:{kind}[{x0},{z0}..{x1},{z1}]";
    }

    /// <summary>Apertura nel muro tra due stanze (porta, arcata) o verso l'esterno (finestra). Il muro e' la linea
    /// 'line' (z se alongX, altrimenti x); 'center' e' la coordinata lungo il muro.</summary>
    public sealed class Opening
    {
        public int a, b;                // stanze (b = -1: esterno)
        public bool alongX;
        public int line, center, width; // decimetri
        public OpenStyle style;
        public float sill, top;         // metri; per le porte sill = 0, top e' il punto piu' alto (apice dell'arco)
        public bool oneSided;           // solo la faccia di 'a' (finestre alte, sopra il tetto della stanza accanto)
        public int variant;

        public bool Pointed => style == OpenStyle.Arco || style == OpenStyle.Arcata || style == OpenStyle.Lancetta;
        public int Lo => center - width / 2;
        public int Hi => center + width / 2;

        /// <summary>Altezza dell'arco sopra l'imposta, in multipli della semiluce: archi acuti piu' o meno slanciati.</summary>
        public float RiseFactor => style == OpenStyle.Lancetta ? 2.3f : style == OpenStyle.Arcata ? 1.45f : 1.3f;

        public static void Defaults(Opening o)
        {
            switch (o.style)
            {
                case OpenStyle.Porta: o.sill = 0f; o.top = 2.25f; break;
                case OpenStyle.PortaLarga: o.sill = 0f; o.top = 2.45f; break;
                case OpenStyle.Arco: o.sill = 0f; o.top = 2.1f + 1.3f * o.width * 0.05f; break;
                case OpenStyle.Arcata: o.sill = 0f; o.top = 5.7f; break;
                case OpenStyle.Finestra: o.sill = 0.95f; o.top = 2.45f; break;
                case OpenStyle.Lancetta: o.sill = 1.9f; o.top = 4.7f; break;
                case OpenStyle.Feritoia: o.sill = 2.35f; o.top = 3.05f; break;
                case OpenStyle.Ascensore: o.sill = 0f; o.top = 2.4f; break;
                case OpenStyle.Passaggio: o.sill = 0f; o.top = 1.3f; break;
            }
        }

        /// <summary>Testa dell'apertura (m) alla distanza dx dal centro: rettangolare per porte e finestre, a due centri
        /// (arco acuto gotico) per Arco, Arcata e Lancetta.</summary>
        public float Head(float dx, float halfW)
        {
            if (!Pointed) return top;
            float rise = RiseFactor * halfW;
            float spring = top - rise;
            // ogni meta' e' un arco di raggio R = a + d con il centro sull'altro lato dell'asse, a distanza d
            float d = (rise * rise - halfW * halfW) / (2f * halfW);
            float R = halfW + d;
            float x = Math.Abs(dx) + d;
            return spring + (float)Math.Sqrt(Math.Max(0f, R * R - x * x));
        }
    }

    public sealed class Seg { public int a0, a1, other; }   // intervallo lungo un muro (dm) e stanza oltre (-1: vuoto)

    public sealed class Plan
    {
        public int seed, floor;
        public string name = "", epithet = "";
        public List<Room> rooms = new List<Room>();
        public List<Opening> openings = new List<Opening>();
        public int start = -1, exit = -1;
        public int minX, minZ, maxX, maxZ;
        public bool startInChurch;
        /// <summary>Il tema del piano: tutte le sue ali lo condividono (vedi PlanGen.PickTheme).</summary>
        public Theme theme = Theme.Albergo;
        /// <summary>Quante ali-coppia (un'ala d'albergo e una di chiesa ciascuna) compongono il piano: vedi PlanGen.Compound.</summary>
        public int units = 1;
        /// <summary>Stanze dove sta un candelabro da accendere (obiettivo del piano).</summary>
        public List<int> objectives = new List<int>();
        /// <summary>Pianta a griglia (PlanGrid.cs): poche stanze grandi con un ruolo (RoomType), da una a quattro porte ciascuna.</summary>
        public bool grid;

        /// <summary>Le stanze di un ruolo (nella pianta a griglia).</summary>
        public List<Room> OfType(RoomType t)
        {
            var res = new List<Room>();
            foreach (var r in rooms) if (r.type == t) res.Add(r);
            return res;
        }

        /// <summary>Quante porte vere (non finestre, non ascensori, non passaggi bassi) ha una stanza.</summary>
        public int DoorCount(Room r)
        {
            int n = 0;
            foreach (var o in openings)
                if ((o.a == r.id || o.b == r.id) && o.b >= 0 && !o.oneSided && o.style != OpenStyle.Ascensore && o.style != OpenStyle.Passaggio) n++;
            return n;
        }

        public const int T = 3;   // spessore dei muri (dm)

        public static Side Opposite(Side s) => s == Side.S ? Side.N : s == Side.N ? Side.S : s == Side.W ? Side.E : Side.W;
        public static bool AlongX(Side s) => s == Side.S || s == Side.N;

        public static int Line(Room r, Side s) => s == Side.S ? r.z0 : s == Side.N ? r.z1 : s == Side.W ? r.x0 : r.x1;

        public static void Span(Room r, Side s, out int lo, out int hi)
        {
            if (AlongX(s)) { lo = r.x0; hi = r.x1; } else { lo = r.z0; hi = r.z1; }
        }

        /// <summary>Tratti di un lato: per ogni intervallo la stanza che sta dall'altra parte (o -1 se e' vuoto).</summary>
        public List<Seg> Segments(Room r, Side s)
        {
            int line = Line(r, s);
            Span(r, s, out int lo, out int hi);
            Side os = Opposite(s);
            var cov = new List<Seg>();
            foreach (var o in rooms)
            {
                if (o == r || Line(o, os) != line) continue;
                Span(o, os, out int olo, out int ohi);
                int a0 = Math.Max(lo, olo), a1 = Math.Min(hi, ohi);
                if (a1 > a0) cov.Add(new Seg { a0 = a0, a1 = a1, other = o.id });
            }
            cov.Sort((p, q) => p.a0.CompareTo(q.a0));
            var res = new List<Seg>();
            int cur = lo;
            foreach (var c in cov)
            {
                if (c.a0 > cur) res.Add(new Seg { a0 = cur, a1 = c.a0, other = -1 });
                res.Add(c);
                cur = c.a1;
            }
            if (cur < hi) res.Add(new Seg { a0 = cur, a1 = hi, other = -1 });
            return res;
        }

        /// <summary>Aperture che bucano il lato 's' della stanza (porte condivise e finestre).</summary>
        public List<Opening> OpeningsOn(Room r, Side s)
        {
            var res = new List<Opening>();
            int line = Line(r, s);
            bool ax = AlongX(s);
            foreach (var o in openings)
            {
                if (o.alongX != ax || o.line != line) continue;
                if (o.a != r.id && o.b != r.id) continue;
                Span(r, s, out int lo, out int hi);
                if (o.Lo < lo || o.Hi > hi) continue;
                res.Add(o);
            }
            return res;
        }

        public void ComputeBounds()
        {
            minX = minZ = int.MaxValue; maxX = maxZ = int.MinValue;
            foreach (var r in rooms)
            {
                minX = Math.Min(minX, r.x0); minZ = Math.Min(minZ, r.z0);
                maxX = Math.Max(maxX, r.x1); maxZ = Math.Max(maxZ, r.z1);
            }
        }

        /// <summary>Stanza di un tipo dentro una sola ala-coppia: con piu' chiese nel piano 'Find' darebbe quella della prima.</summary>
        public Room FindIn(RoomKind k, int unit)
        {
            foreach (var r in rooms) if (r.kind == k && r.unit == unit) return r;
            return null;
        }

        public Room Find(RoomKind k)
        {
            foreach (var r in rooms) if (r.kind == k) return r;
            return null;
        }

        /// <summary>Distanza in numero di porte da 'from' a ogni stanza (-1 se irraggiungibile).</summary>
        public int[] Hops(int from)
        {
            var d = new int[rooms.Count];
            for (int i = 0; i < d.Length; i++) d[i] = -1;
            var q = new Queue<int>();
            d[from] = 0; q.Enqueue(from);
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                foreach (var o in openings)
                {
                    if (o.b < 0 || o.oneSided) continue;
                    int v = o.a == u ? o.b : o.b == u ? o.a : -1;
                    if (v < 0 || d[v] >= 0) continue;
                    d[v] = d[u] + 1;
                    q.Enqueue(v);
                }
            }
            return d;
        }

        /// <summary>Distanza a piedi (m) tra i centri delle stanze lungo le porte (Dijkstra sul grafo stanza-porta).</summary>
        public float[] WalkDist(int from)
        {
            var dist = new float[rooms.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = float.MaxValue;
            dist[from] = 0f;
            var done = new bool[rooms.Count];
            for (int it = 0; it < rooms.Count; it++)
            {
                int u = -1; float best = float.MaxValue;
                for (int i = 0; i < dist.Length; i++) if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u < 0) break;
                done[u] = true;
                foreach (var o in openings)
                {
                    if (o.b < 0 || o.oneSided) continue;
                    int v = o.a == u ? o.b : o.b == u ? o.a : -1;
                    if (v < 0) continue;
                    float ox = o.alongX ? o.center * 0.1f : o.line * 0.1f, oz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                    float du = Dist(rooms[u], ox, oz), dv = Dist(rooms[v], ox, oz);
                    if (dist[u] + du + dv < dist[v]) dist[v] = dist[u] + du + dv;
                }
            }
            return dist;
        }

        static float Dist(Room r, float x, float z)
        {
            float dx = x - r.CenterX, dz = z - r.CenterZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
