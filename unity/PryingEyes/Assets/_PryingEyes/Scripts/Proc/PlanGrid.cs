using System;
using System.Collections.Generic;

namespace PryingEyes.Proc
{
    /// <summary>Pianta a griglia: tredici-diciassette ambienti principali, grandi sale e percorsi su più quote;
    /// grandi su una griglia di celle (1x1, 2x1, 1x2, 2x2), ciascuna con un ruolo (RoomType) e da una a quattro porte, come le stanze di
    /// The Binding of Isaac. Emette lo stesso Plan di prima (geometria intera in decimetri, aperture, ascensori, finestre): costruttore,
    /// temi, luci, navmesh e nemici restano quelli. Il ruolo decide che cosa c'e' dentro: l'arena di una candela, il negozio, la
    /// biblioteca dei perk, la stanza segreta, il boss che sorveglia l'uscita.
    ///
    /// Come si fa: (1) si fa crescere un insieme di rettangoli di celle, ognuno attaccato a uno gia' messo; (2) si sceglie la stanza di
    /// partenza sul bordo e si disegna un albero di porte da li', poi i ruoli sulle foglie e sulle stanze lontane, poi qualche anello
    /// di porte in piu'; (3) due zone (arrivo e boss) fanno le due "ali" del tema, ciascuna con la sua tavolozza di arredi; (4) le stanze
    /// diventano Room, le porte Opening, e si aggiungono ascensori, finestre e un paio di passaggi bassi.</summary>
    public static partial class PlanGen
    {
        /// <summary>Quale generatore usa Generate: la griglia (default) o le ali d'albergo e di chiesa di prima (il flag -oldplan del gioco).</summary>
        public static bool UseGrid = true;
        /// <summary>Pianta del nuovo ciclo a ondate: meno aree, più grandi, senza stanza segreta a passaggio basso.
        /// Attivata dal nuovo ciclo quando sarà integrato; il laboratorio la verifica con il comando survival.</summary>
        public static bool SurvivalLayout;

        /// <summary>Dimensioni possibili di una colonna o di una riga della griglia (dm): le stanze sullo stesso allineamento le condividono,
        /// cosi' i muri combaciano e le stanze non sono tutte uguali.</summary>
        static readonly int[] CellSizes = { 144, 160, 176, 192 };

        sealed class GAdj
        {
            public int a, b;                                         // indici delle stanze
            public bool alongX;                                      // il muro condiviso corre lungo x
            public readonly List<int> centers = new List<int>();     // centri delle celle lungo il muro condiviso (dm)
            public bool door, secret;                                // porta aperta; passaggio basso (stanza segreta)
        }

        sealed class GRoom
        {
            public int cx, cy, cw, ch;                               // celle
            public RoomType type;
            public RoomKind kind;
            public int group;                                        // 0 = zona dell'arrivo, 1 = zona del boss
            public int deg;                                          // porte
            public int hops;                                         // porte dall'arrivo nell'albero
            public bool hull;                                        // ha un lato sul vuoto
            public Room room;
            public readonly List<GAdj> adj = new List<GAdj>();
            public int Cells => cw * ch;
            public bool Special => type != RoomType.Normale && type != RoomType.Guardia;
        }

        // ------------------------------------------------------------------ costruzione
        static Plan BuildGrid(Rng r, int seed, int floor, bool simple)
        {
            var theme = PickTheme(floor, new Rng(unchecked(seed * 73 + floor * 1009 + 5)));
            int n = SurvivalLayout ? Math.Max(7, Math.Min(9, 7 + Math.Max(0, floor - 1) / 4) - (simple ? 1 : 0)) :
                Math.Min(17, 13 + Math.Max(0, floor - 1) / 3) - (simple ? 1 : 0);
            int gw = 7, gh = n >= 15 ? 6 : 5;

            var occ = new int[gw, gh];
            var gs = GrowLayout(r, gw, gh, n, occ);
            if (gs == null) { LastFailure = "griglia: crescita"; return null; }

            // colonne e righe: misure a caso, prefisso delle coordinate
            var colW = new int[gw]; var rowH = new int[gh];
            var sizes = SurvivalLayout ? new[] { 176, 192, 208, 224 } : CellSizes;
            for (int i = 0; i < gw; i++) colW[i] = r.Pick(sizes);
            for (int j = 0; j < gh; j++) rowH[j] = r.Pick(sizes);
            var colX = new int[gw + 1]; var rowZ = new int[gh + 1];
            for (int i = 0; i < gw; i++) colX[i + 1] = colX[i] + colW[i];
            for (int j = 0; j < gh; j++) rowZ[j + 1] = rowZ[j] + rowH[j];

            var adjs = BuildAdjacency(gs, colX, rowZ, colW, rowH);
            MarkHull(gs, occ, gw, gh);

            // ---- partenza: una stanza sul bordo (con un lato sul vuoto), non enorme
            var starts = gs.FindAll(g => g.hull && g.Cells <= 2);
            if (SurvivalLayout)
            {
                var large = gs.FindAll(g => g.hull && g.Cells >= 2);
                if (large.Count > 0) starts = large;
            }
            if (starts.Count == 0) { LastFailure = "griglia: nessuna partenza"; return null; }
            var start = starts[r.Range(0, starts.Count)];
            start.type = RoomType.Partenza;

            // ---- albero delle porte, a partire dall'arrivo: spesso si prosegue dall'ultima stanza aggiunta (corridoi lunghi), a volte da una a caso
            var inTree = new bool[gs.Count];
            inTree[gs.IndexOf(start)] = true;
            GRoom last = start;
            for (int added = 1; added < gs.Count; added++)
            {
                var cand = new List<GAdj>();
                foreach (var e in adjs)
                {
                    if (inTree[e.a] == inTree[e.b]) continue;
                    var from = inTree[e.a] ? gs[e.a] : gs[e.b];
                    if (from.deg >= (from == start ? 3 : 4)) continue;
                    cand.Add(e);
                }
                if (cand.Count == 0) { LastFailure = "griglia: albero"; return null; }
                var pool = cand;
                if (r.Chance(0.5))
                {
                    int li = gs.IndexOf(last);
                    var near = cand.FindAll(e => e.a == li || e.b == li);
                    if (near.Count > 0) pool = near;
                }
                var pick = pool[r.Range(0, pool.Count)];
                pick.door = true;
                int nv = inTree[pick.a] ? pick.b : pick.a;
                inTree[nv] = true;
                gs[pick.a].deg++; gs[pick.b].deg++;
                last = gs[nv];
            }
            TreeHops(gs, start);

            // ---- ruoli sulle foglie e sulle stanze lontane
            var leaves = gs.FindAll(g => g.deg == 1 && g != start);
            GRoom boss = null; double bs = -1;
            foreach (var g in leaves)
            {
                if (!g.hull || g.hops < (SurvivalLayout ? 2 : 3)) continue;
                double s = g.hops + (g.Cells - 1) * 0.7 + r.Value() * 0.8;
                if (s > bs) { bs = s; boss = g; }
            }
            if (boss == null) { LastFailure = "griglia: nessun boss"; return null; }
            boss.type = RoomType.Boss;

            var secret = SurvivalLayout ? null : PickRole(gs, r, g => g.deg == 1 && g.type == RoomType.Normale && g.Cells == 1, g => 0.0);
            if (secret != null)
            {
                secret.type = RoomType.Segreta;
                foreach (var e in secret.adj) if (e.door) e.secret = true;
            }
            var shop = PickRole(gs, r, g => g.type == RoomType.Normale && g.deg <= 2 && (SurvivalLayout || g.Cells == 1), g => (g.deg == 1 ? 2.0 : 0.0) + (g.hops >= 2 && g.hops <= 5 ? 1.0 : 0.0));
            if (shop == null) { LastFailure = "griglia: nessun negozio"; return null; }
            shop.type = RoomType.Negozio;
            var library = PickRole(gs, r, g => g.type == RoomType.Normale && g.deg <= 2, g => (g.deg == 1 ? 2.0 : 0.0) + Math.Min(g.hops, 5) * 0.4 - Math.Abs(g.Cells - 2) * 0.35);
            if (library == null) { LastFailure = "griglia: nessuna biblioteca"; return null; }
            library.type = RoomType.Biblioteca;

            // le arene delle candele: lontane dall'arrivo e tra loro (campionamento del punto piu' lontano), non troppo grandi per sigillarle
            int candles = SurvivalLayout ? 2 : Math.Min(4, 2 + Math.Max(0, floor - 1) / 3);
            var arenas = new List<GRoom>();
            var arenaDist = new List<int[]>();
            for (int k = 0; k < candles; k++)
            {
                GRoom best = null; double bd = -1;
                foreach (var g in gs)
                {
                    if (g.type != RoomType.Normale || g.Cells > 4) continue;
                    long area = (long)(colX[g.cx + g.cw] - colX[g.cx]) * (rowZ[g.cy + g.ch] - rowZ[g.cy]);
                    if (!SurvivalLayout && area > 180000) continue;
                    double m = g.hops;
                    foreach (var d in arenaDist) m = Math.Min(m, d[gs.IndexOf(g)]);
                    m *= 0.7 + r.Value() * 0.6;
                    if (k==0 && g.cw==2 && g.ch==2) m+=20;                     // il grande atrio deve ospitare un'orda, non essere solo un corridoio scenografico
                    if (m > bd) { bd = m; best = g; }
                }
                if (best == null) break;
                best.type = RoomType.Candela;
                arenas.Add(best);
                arenaDist.Add(TreeDist(gs, best));
            }
            if (arenas.Count < 2) { LastFailure = "griglia: poche arene"; return null; }
            foreach (var g in gs) if (g.type == RoomType.Normale) g.type = RoomType.Guardia;

            // ---- anelli: qualche porta in piu' tra stanze che non siano foglie speciali (cosi' si gira attorno alle arene)
            var loops = adjs.FindAll(e => !e.door && LoopOk(gs[e.a], start) && LoopOk(gs[e.b], start));
            for (int i = loops.Count - 1; i > 0; i--) { int j = r.Range(0, i + 1); var t = loops[i]; loops[i] = loops[j]; loops[j] = t; }
            int maxLoops = Math.Max(3, gs.Count / 2), made = 0;
            foreach (var e in loops)
            {
                if (made >= maxLoops) break;
                if (!LoopOk(gs[e.a], start) || !LoopOk(gs[e.b], start)) continue;
                if (!r.Chance(0.85)) continue;
                e.door = true; gs[e.a].deg++; gs[e.b].deg++; made++;
            }

            // ---- le due zone: arrivo e boss (distanza in porte, a parimenti' va all'arrivo)
            int si = gs.IndexOf(start), bi = gs.IndexOf(boss);
            var dA = GraphDist(gs, si); var dB = GraphDist(gs, bi);
            for (int i = 0; i < gs.Count; i++) gs[i].group = dB[i] < dA[i] ? 1 : 0;
            start.group = 0; boss.group = 1;
            BalanceGroups(gs, dB, si, bi);

            bool churchTheme = theme != Theme.Teatro;
            bool startChurch = churchTheme && r.Chance(0.5);                       // la zona dell'arrivo e' la chiesa (e quella del boss l'albergo)
            var used = new Dictionary<RoomKind, int>();

            // ---- la pianta vera
            var p = new Plan { grid = true, theme = theme, seed = seed, floor = floor, units = 1, startInChurch = startChurch };
            foreach (var g in gs)
            {
                bool church = churchTheme && ((g.group == 0) == startChurch);
                int wing = theme == Theme.Teatro ? g.group : (church ? 1 : 0);
                g.kind = PickGridKind(theme, wing, church, g.type, g.Cells, used, r);
                var room = new Room
                {
                    id = p.rooms.Count, kind = g.kind, type = g.type, church = church, wing = wing, unit = 0, seed = r.Range(1, 1 << 30),
                    x0 = colX[g.cx], z0 = rowZ[g.cy], x1 = colX[g.cx + g.cw], z1 = rowZ[g.cy + g.ch],
                };
                SetKindDefaults(room);
                room.upperGallery = room.W >= 160 && room.D >= 144 &&
                    (room.type == RoomType.Candela || room.type == RoomType.Boss || room.type == RoomType.Guardia || (SurvivalLayout && room.type == RoomType.Partenza));
                if (room.W >= 240 && room.D >= 240 && room.type != RoomType.Segreta)
                    room.upperGallery = true;
                if (room.upperGallery)
                {
                    room.wallH = Math.Max(room.wallH, room.GalleryLevels == 2 ? 11.6f : 8.2f);
                    room.ridgeH = Math.Max(room.ridgeH, room.wallH);
                }
                if (room.ceil == CeilKind.Gable || room.ceil == CeilKind.BarrelRound || room.ceil == CeilKind.BarrelPointed)
                    room.ridgeAlongX = room.W > room.D || (room.W == room.D && r.Chance(0.5));
                g.room = room;
                p.rooms.Add(room);
            }

            // porte: ogni arco dell'albero (e degli anelli) e' una porta; verso la stanza segreta un passaggio basso
            foreach (var e in adjs)
            {
                if (!e.door) continue;
                var ra = gs[e.a].room; var rb = gs[e.b].room;
                bool churchDoor = ra.church || rb.church;
                bool toBoss = ra.type == RoomType.Boss || rb.type == RoomType.Boss;
                OpenStyle st = e.secret ? OpenStyle.Passaggio : churchDoor ? OpenStyle.Arco : OpenStyle.PortaLarga;
                int width = e.secret ? 12 : toBoss ? 30 : churchDoor ? 26 : 24;
                int at = e.centers[r.Range(0, e.centers.Count)];
                if (Connect(p.openings, ra, rb, st, width, at, 3) == null) { LastFailure = "griglia: porta"; return null; }
            }

            // ascensori: l'arrivo nella stanza di partenza, l'uscita in quella del boss
            if (!AddLift(p, r, start.room)) { LastFailure = "griglia: ascensore di arrivo"; return null; }
            p.start = p.rooms.Count - 1;
            if (!AddLift(p, r, boss.room)) { LastFailure = "griglia: ascensore di uscita"; return null; }
            p.exit = p.rooms.Count - 1;

            ShapeSpaces(p);
            AddWindows(p, r);
            AddThemeWindows(p, r);
            AddGridPassages(p, r);
            p.openings.RemoveAll(o =>
            {
                if (o.sill < 0.5f && !o.oneSided) return false;
                var room = p.rooms[o.a];
                if (room.cornerRadius <= 0f) return false;
                float lo = (o.alongX ? room.x0 : room.z0) * 0.1f + room.cornerRadius + 0.2f;
                float hi = (o.alongX ? room.x1 : room.z1) * 0.1f - room.cornerRadius - 0.2f;
                return o.Lo * 0.1f < lo || o.Hi * 0.1f > hi;
            });
            foreach (var a in arenas) p.objectives.Add(a.room.id);
            p.ComputeBounds();
            foreach (var room in p.rooms) { room.label = room.kind.ToString(); room.variant = r.Range(0, 1000); }
            p.epithet = Epithet(p, r);
            p.name = "Piano " + floor;
            return p;
        }

        static void ShapeSpaces(Plan plan)
        {
            float Radius(Room room)
            {
                float radius = Math.Min(room.W, room.D) * 0.05f - 1.7f;
                foreach (var opening in plan.openings)
                {
                    if (opening.a != room.id && opening.b != room.id) continue;
                    int lo = opening.alongX ? room.x0 : room.z0, hi = opening.alongX ? room.x1 : room.z1;
                    radius = Math.Min(radius, Math.Min(opening.Lo - lo, hi - opening.Hi) * 0.1f - 0.4f);
                }
                return Math.Max(0f, radius);
            }
            Room rotunda = null;
            float best = 0f;
            foreach (var room in plan.rooms)
            {
                if (room.type != RoomType.Guardia || Math.Min(room.W, room.D) < 128) continue;
                if (room.GalleryLevels==2) continue; // la sala più grande resta un atrio a tre quote; la Rotonda può essere anche ovale
                float radius = Radius(room);
                if (radius > best) { best = radius; rotunda = room; }
            }
            if (rotunda != null && best >= 2.5f)
            {
                rotunda.kind = RoomKind.Rotonda; rotunda.cornerRadius = best;
                rotunda.upperGallery = false; rotunda.ceil = CeilKind.Flat;
                rotunda.wallH = Math.Max(rotunda.wallH, 6.2f); rotunda.ridgeH = rotunda.wallH;
            }
            bool garden = false;
            foreach (var room in plan.rooms)
            {
                if (room.type != RoomType.Guardia || room == rotunda) continue;
                if (Math.Max(room.W, room.D) >= Math.Min(room.W, room.D) * 1.65f)
                {
                    room.kind = RoomKind.Galleria;
                    room.wallH = Math.Max(room.wallH, 4.8f); room.ridgeH = Math.Max(room.ridgeH, room.wallH);
                }
                else if (!garden && !room.church && plan.theme == Theme.Albergo)
                {
                    room.kind = RoomKind.GiardinoInverno; garden = true;
                    room.cornerRadius = Math.Min(1.5f, Radius(room));
                    room.wallH = Math.Max(room.wallH, 5.8f); room.ridgeH = Math.Max(room.ridgeH, room.wallH);
                }
            }
        }

        /// <summary>Foglie speciali e arene non fanno anelli: la partenza si limita a tre porte, tutte le altre a quattro.</summary>
        static bool LoopOk(GRoom g, GRoom start)
        {
            if (g.type != RoomType.Guardia && g.type != RoomType.Candela && g.type != RoomType.Partenza) return false;
            return g.deg < (g == start ? 3 : 4);
        }

        /// <summary>La stanza che soddisfa 'ok' con il punteggio migliore (piu' un po' di caso); nulla se non ce n'e'.</summary>
        static GRoom PickRole(List<GRoom> gs, Rng r, Func<GRoom, bool> ok, Func<GRoom, double> score)
        {
            GRoom best = null; double bs = double.MinValue;
            foreach (var g in gs)
            {
                if (!ok(g)) continue;
                double s = score(g) + r.Value() * 1.2;
                if (s > bs) { bs = s; best = g; }
            }
            return best;
        }

        // ------------------------------------------------------------------ crescita
        static List<GRoom> GrowLayout(Rng r, int gw, int gh, int n, int[,] occ)
        {
            for (int x = 0; x < gw; x++) for (int y = 0; y < gh; y++) occ[x, y] = -1;
            var list = new List<GRoom>();
            bool Fits(int x, int y, int w, int h)
            {
                if (x < 0 || y < 0 || x + w > gw || y + h > gh) return false;
                for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) if (occ[x + i, y + j] >= 0) return false;
                return true;
            }
            void Put(int x, int y, int w, int h)
            {
                for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) occ[x + i, y + j] = list.Count;
                list.Add(new GRoom { cx = x, cy = y, cw = w, ch = h });
            }
            bool Touch(int x, int y, int w, int h, GRoom p)
            {
                bool rows = Math.Min(y + h, p.cy + p.ch) > Math.Max(y, p.cy);
                bool cols = Math.Min(x + w, p.cx + p.cw) > Math.Max(x, p.cx);
                return (rows && (x + w == p.cx || p.cx + p.cw == x)) || (cols && (y + h == p.cy || p.cy + p.ch == y));
            }
            Put(r.Range(1, gw - 1), r.Range(1, gh - 1), 1, 1);
            int big = 0, huge = 0;
            for (int guard = 0; list.Count < n && guard < 4000; guard++)
            {
                var parent = r.Chance(0.5) ? list[list.Count - 1 - r.Range(0, Math.Min(3, list.Count))] : list[r.Range(0, list.Count)];
                int shape = r.Weighted(new[] { 0.30, 0.24, 0.24, 0.22 });       // 1x1, 2x1, 1x2, 2x2
                if (huge==0 && list.Count<4) shape=3;                         // almeno un grande atrio prima di riempire la griglia di stanze minori
                if (SurvivalLayout && huge < 2 && list.Count < 5) shape = 3;
                int w = shape == 1 || shape == 3 ? 2 : 1, h = shape == 2 || shape == 3 ? 2 : 1;
                if (w * h > 1 && big >= 8) w = h = 1;
                if (w == 2 && h == 2 && huge >= 3) h = 1;
                var cand = new List<int[]>();
                for (int x = parent.cx - w; x <= parent.cx + parent.cw; x++)
                    for (int y = parent.cy - h; y <= parent.cy + parent.ch; y++)
                        if (Fits(x, y, w, h) && Touch(x, y, w, h, parent)) cand.Add(new[] { x, y });
                if (cand.Count == 0) continue;
                var c = cand[r.Range(0, cand.Count)];
                Put(c[0], c[1], w, h);
                if (w * h > 1) big++;
                if (w == 2 && h == 2) huge++;
            }
            return list.Count == n ? list : null;
        }

        /// <summary>Le coppie di stanze che si toccano su un tratto di muro, con i centri delle celle lungo quel tratto (dove si puo' aprire una porta).</summary>
        static List<GAdj> BuildAdjacency(List<GRoom> gs, int[] colX, int[] rowZ, int[] colW, int[] rowH)
        {
            var res = new List<GAdj>();
            for (int i = 0; i < gs.Count; i++)
                for (int j = i + 1; j < gs.Count; j++)
                {
                    var a = gs[i]; var b = gs[j];
                    int ro0 = Math.Max(a.cy, b.cy), ro1 = Math.Min(a.cy + a.ch, b.cy + b.ch);
                    int co0 = Math.Max(a.cx, b.cx), co1 = Math.Min(a.cx + a.cw, b.cx + b.cw);
                    GAdj e = null;
                    if (ro1 > ro0 && (a.cx + a.cw == b.cx || b.cx + b.cw == a.cx))
                    {
                        e = new GAdj { a = i, b = j, alongX = false };
                        for (int y = ro0; y < ro1; y++) e.centers.Add(rowZ[y] + rowH[y] / 2);
                    }
                    else if (co1 > co0 && (a.cy + a.ch == b.cy || b.cy + b.ch == a.cy))
                    {
                        e = new GAdj { a = i, b = j, alongX = true };
                        for (int x = co0; x < co1; x++) e.centers.Add(colX[x] + colW[x] / 2);
                    }
                    if (e == null) continue;
                    res.Add(e);
                    a.adj.Add(e); b.adj.Add(e);
                }
            return res;
        }

        /// <summary>Una stanza e' "sul bordo" se una sua cella confina con il vuoto o con l'esterno della griglia: li' possono stare un ascensore e le finestre.</summary>
        static void MarkHull(List<GRoom> gs, int[,] occ, int gw, int gh)
        {
            for (int k = 0; k < gs.Count; k++)
            {
                var g = gs[k];
                for (int x = g.cx; x < g.cx + g.cw; x++)
                    for (int y = g.cy; y < g.cy + g.ch; y++)
                        foreach (var d in new[] { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } })
                        {
                            int nx = x + d[0], ny = y + d[1];
                            if (nx < 0 || ny < 0 || nx >= gw || ny >= gh || occ[nx, ny] < 0) g.hull = true;
                        }
            }
        }

        // ------------------------------------------------------------------ distanze nel grafo delle porte
        static void TreeHops(List<GRoom> gs, GRoom start)
        {
            var d = GraphDist(gs, gs.IndexOf(start));
            for (int i = 0; i < gs.Count; i++) gs[i].hops = d[i];
        }

        static int[] TreeDist(List<GRoom> gs, GRoom from) => GraphDist(gs, gs.IndexOf(from));

        /// <summary>Distanza in porte da una stanza a tutte le altre, solo lungo gli archi gia' aperti.</summary>
        static int[] GraphDist(List<GRoom> gs, int from)
        {
            var d = new int[gs.Count];
            for (int i = 0; i < d.Length; i++) d[i] = int.MaxValue / 2;
            var q = new Queue<int>();
            d[from] = 0; q.Enqueue(from);
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                foreach (var e in gs[u].adj)
                {
                    if (!e.door) continue;
                    int v = e.a == u ? e.b : e.a;
                    if (d[v] <= d[u] + 1) continue;
                    d[v] = d[u] + 1; q.Enqueue(v);
                }
            }
            return d;
        }

        /// <summary>Ognuna delle due zone ha almeno tre stanze (se il piano e' piccolo, due): sposta quelle piu' vicine al boss.</summary>
        static void BalanceGroups(List<GRoom> gs, int[] dB, int si, int bi)
        {
            int want = Math.Max(3, gs.Count * 3 / 10);
            var ids = new List<int>();
            for (int i = 0; i < gs.Count; i++) if (i != si && i != bi) ids.Add(i);
            ids.Sort((x, y) => dB[x].CompareTo(dB[y]));
            int inB = 0; foreach (var g in gs) if (g.group == 1) inB++;
            foreach (int i in ids)
            {
                if (inB >= want) break;
                if (gs[i].group == 0) { gs[i].group = 1; inB++; }
            }
            int inA = gs.Count - inB;
            if (inA < 3)
            {
                ids.Reverse();
                foreach (int i in ids)
                {
                    if (inA >= 3) break;
                    if (gs[i].group == 1) { gs[i].group = 0; inA++; }
                }
            }
        }

        // ------------------------------------------------------------------ tavolozze
        struct KC
        {
            public RoomKind k; public double w; public int lo, hi;
            public KC(RoomKind k, double w, int lo = 1, int hi = 4) { this.k = k; this.w = w; this.lo = lo; this.hi = hi; }
        }

        /// <summary>Le stanze possibili per un ruolo in una zona del tema, con il peso e quante celle puo' occupare. La zona 0 del
        /// teatro e' la sala, la 1 il palcoscenico; nell'albergo e nel sanatorio la chiesa sta al posto della seconda ala di prima.</summary>
        static KC[] Palette(Theme theme, int wing, bool church, RoomType type)
        {
            if (church)
                switch (type)
                {
                    case RoomType.Partenza: return new[] { new KC(RoomKind.Sacrestia, 2, 1, 1), new KC(RoomKind.Cappella, 2, 1, 1), new KC(RoomKind.Cripta, 1, 1, 2) };
                    case RoomType.Boss: return new[] { new KC(RoomKind.Presbiterio, 3), new KC(RoomKind.Navata, 2, 2) };
                    case RoomType.Candela: return new[] { new KC(RoomKind.Navata, 3), new KC(RoomKind.Nartece, 1, 1, 2), new KC(RoomKind.Presbiterio, 1, 1, 2), new KC(RoomKind.Cripta, 1, 1, 2) };
                    case RoomType.Negozio: return new[] { new KC(RoomKind.Sacrestia, 3, 1, 1), new KC(RoomKind.Cappella, 1, 1, 1) };
                    case RoomType.Biblioteca: return new[] { new KC(RoomKind.Sacrestia, 2, 1, 2), new KC(RoomKind.Cappella, 1, 1, 1) };
                    case RoomType.Segreta: return new[] { new KC(RoomKind.Cripta, 2, 1, 1), new KC(RoomKind.Campanile, 1, 1, 1) };
                    default: return new[] { new KC(RoomKind.Navata, 2), new KC(RoomKind.Nartece, 2, 1, 2), new KC(RoomKind.Cappella, 2, 1, 1), new KC(RoomKind.Sacrestia, 2, 1, 1), new KC(RoomKind.Cripta, 1, 1, 2), new KC(RoomKind.Campanile, 1, 1, 1) };
                }
            switch (theme)
            {
                case Theme.Teatro:
                    if (wing == 0)
                        switch (type)
                        {
                            case RoomType.Partenza: return new[] { new KC(RoomKind.Foyer, 1, 1, 2) };
                            case RoomType.Boss: return new[] { new KC(RoomKind.Platea, 1, 2) };
                            case RoomType.Candela: return new[] { new KC(RoomKind.Platea, 3), new KC(RoomKind.Foyer, 1, 1, 2) };
                            case RoomType.Negozio: return new[] { new KC(RoomKind.Ridotto, 3, 1, 1), new KC(RoomKind.Foyer, 1, 1, 1) };
                            case RoomType.Biblioteca: return new[] { new KC(RoomKind.Ridotto, 2, 1, 2), new KC(RoomKind.Foyer, 1, 1, 2) };
                            case RoomType.Segreta: return new[] { new KC(RoomKind.Palco, 2, 1, 1), new KC(RoomKind.Ridotto, 1, 1, 1) };
                            default: return new[] { new KC(RoomKind.Platea, 2), new KC(RoomKind.Foyer, 2, 1, 2), new KC(RoomKind.Ridotto, 2, 1, 1), new KC(RoomKind.Palco, 1, 1, 1) };
                        }
                    switch (type)
                    {
                        case RoomType.Partenza: return new[] { new KC(RoomKind.Camerino, 1, 1, 1), new KC(RoomKind.Deposito, 1, 1, 1) };
                        case RoomType.Boss: return new[] { new KC(RoomKind.Palcoscenico, 3, 2), new KC(RoomKind.Palcoscenico, 1) };
                        case RoomType.Candela: return new[] { new KC(RoomKind.Palcoscenico, 3), new KC(RoomKind.Deposito, 1, 1, 2), new KC(RoomKind.Camerino, 1, 1, 1) };
                        case RoomType.Negozio: return new[] { new KC(RoomKind.Camerino, 1, 1, 1) };
                        case RoomType.Biblioteca: return new[] { new KC(RoomKind.Camerino, 1, 1, 2), new KC(RoomKind.Deposito, 1, 1, 2) };
                        case RoomType.Segreta: return new[] { new KC(RoomKind.Deposito, 1, 1, 1), new KC(RoomKind.Camerino, 1, 1, 1) };
                        default: return new[] { new KC(RoomKind.Camerino, 3, 1, 1), new KC(RoomKind.Deposito, 3, 1, 2), new KC(RoomKind.Palcoscenico, 1) };
                    }
                case Theme.Sanatorio:
                    switch (type)
                    {
                        case RoomType.Partenza: return new[] { new KC(RoomKind.Accettazione, 1, 1, 2) };
                        case RoomType.Boss: return new[] { new KC(RoomKind.SalaOperatoria, 3), new KC(RoomKind.Refettorio, 1, 2) };
                        case RoomType.Candela: return new[] { new KC(RoomKind.Corsia, 3, 2), new KC(RoomKind.Refettorio, 2), new KC(RoomKind.Accettazione, 1), new KC(RoomKind.SalaOperatoria, 1) };
                        case RoomType.Negozio: return new[] { new KC(RoomKind.SalaMedici, 3, 1, 1), new KC(RoomKind.Lavanderia, 1, 1, 1) };
                        case RoomType.Biblioteca: return new[] { new KC(RoomKind.Archivio, 3, 1, 2), new KC(RoomKind.SalaMedici, 1, 1, 1) };
                        case RoomType.Segreta: return new[] { new KC(RoomKind.Cella, 2, 1, 1), new KC(RoomKind.Obitorio, 2, 1, 1) };
                        default: return new[] { new KC(RoomKind.Degenza, 3, 1, 1), new KC(RoomKind.Corsia, 2, 2), new KC(RoomKind.Lavanderia, 2, 1, 1), new KC(RoomKind.Refettorio, 1), new KC(RoomKind.Obitorio, 1, 1, 1) };
                    }
                default:
                    switch (type)
                    {
                        case RoomType.Partenza: return new[] { new KC(RoomKind.Atrio, 1, 1, 2) };
                        case RoomType.Boss: return new[] { new KC(RoomKind.SalaBallo, 3, 2), new KC(RoomKind.Ristorante, 2, 2), new KC(RoomKind.Atrio, 1, 2) };
                        case RoomType.Candela: return new[] { new KC(RoomKind.Ristorante, 3), new KC(RoomKind.SalaBallo, 2, 2), new KC(RoomKind.Atrio, 1), new KC(RoomKind.Cucina, 1, 1, 2) };
                        case RoomType.Negozio: return new[] { new KC(RoomKind.Suite, 3, 1, 1), new KC(RoomKind.Camera, 1, 1, 1) };
                        case RoomType.Biblioteca: return new[] { new KC(RoomKind.Suite, 2, 1, 2), new KC(RoomKind.Camera, 1, 1, 1) };
                        case RoomType.Segreta: return new[] { new KC(RoomKind.Servizio, 1, 1, 1) };
                        default: return new[] { new KC(RoomKind.Suite, 2, 1, 1), new KC(RoomKind.Camera, 2, 1, 1), new KC(RoomKind.Cucina, 2, 1, 2), new KC(RoomKind.Ristorante, 2), new KC(RoomKind.Atrio, 1, 1, 2), new KC(RoomKind.SalaBallo, 1, 2) };
                    }
            }
        }

        /// <summary>La funzione di una stanza: dalla tavolozza del suo ruolo, filtrata per numero di celle; i tipi gia' usati pesano meno.</summary>
        static RoomKind PickGridKind(Theme theme, int wing, bool church, RoomType type, int cells, Dictionary<RoomKind, int> used, Rng r)
        {
            var pal = Palette(theme, wing, church, type);
            var ok = new List<KC>();
            foreach (var c in pal) if (cells >= c.lo && cells <= c.hi) ok.Add(c);
            if (ok.Count == 0) ok.AddRange(pal);
            var w = new double[ok.Count];
            for (int i = 0; i < w.Length; i++) w[i] = ok[i].w / (1.0 + 0.8 * (used.TryGetValue(ok[i].k, out int u) ? u : 0));
            var kind = ok[r.Weighted(w)].k;
            used[kind] = used.TryGetValue(kind, out int uu) ? uu + 1 : 1;
            return kind;
        }

        // ------------------------------------------------------------------ passaggi bassi
        /// <summary>Un paio di scorciatoie a carponi tra stanze che si toccano e non hanno gia' una porta: i nemici non ci passano (la navmesh
        /// non c'e' sotto 1,9 m), il giocatore si'. Mai nelle foglie speciali, che restano foglie.</summary>
        static void AddGridPassages(Plan p, Rng rng)
        {
            var candidates = new List<Opening>();
            foreach (var room in p.rooms)
            {
                if (room.IsLift || room.cornerRadius > 0f || room.type == RoomType.Segreta || room.type == RoomType.Boss || room.type == RoomType.Negozio || room.type == RoomType.Biblioteca) continue;
                foreach (var side in AllSides)
                    foreach (var seg in p.Segments(room, side))
                    {
                        if (seg.other <= room.id) continue;
                        var other = p.rooms[seg.other];
                        if (other.cornerRadius > 0f) continue;
                        if (other.IsLift || other.type == RoomType.Segreta || other.type == RoomType.Boss || other.type == RoomType.Negozio || other.type == RoomType.Biblioteca) continue;
                        if (seg.a1 - seg.a0 < 24) continue;
                        int line = Plan.Line(room, side);
                        bool ax = Plan.AlongX(side);
                        for (int center = seg.a0 + 8; center <= seg.a1 - 8; center += 4)
                        {
                            bool clear = true;
                            foreach (var o in p.openings)
                                if (o.line == line && o.alongX == ax && center + 10 > o.Lo && center - 10 < o.Hi) { clear = false; break; }
                            if (clear) candidates.Add(new Opening { a = room.id, b = seg.other, alongX = ax, line = line, center = center, width = 12, style = OpenStyle.Passaggio });
                        }
                    }
            }
            int count = Math.Min(2, candidates.Count);
            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var o = candidates[rng.Range(0, candidates.Count)];
                Opening.Defaults(o);
                p.openings.Add(o);
                candidates.RemoveAll(c => c.alongX == o.alongX && c.line == o.line && Math.Abs(c.center - o.center) < 24);
            }
        }

        // ------------------------------------------------------------------ controlli
        /// <summary>Oltre ai controlli di Validate: ruoli al loro posto, da una a quattro porte per stanza, ogni candela in una zona che si sigilla.</summary>
        public static bool ValidateGrid(Plan p, out string why)
        {
            why = "";
            if (!p.grid) return true;
            int start = -1, boss = -1, shops = 0, libs = 0;
            foreach (var r in p.rooms)
            {
                if (r.IsLift) continue;
                int doors = p.DoorCount(r);
                int links = 0;
                foreach (var o in p.openings) if ((o.a == r.id || o.b == r.id) && o.b >= 0 && !o.oneSided && o.style != OpenStyle.Ascensore) links++;
                if (links == 0) { why = "stanza senza porte " + r; return false; }
                if (doors > 4) { why = "piu' di quattro porte " + r; return false; }
                if (r.type == RoomType.Partenza) start = r.id;
                if (r.type == RoomType.Boss) boss = r.id;
                if (r.type == RoomType.Negozio) shops++;
                if (r.type == RoomType.Biblioteca) libs++;
            }
            if (start < 0 || boss < 0 || start == boss) { why = "partenza o boss mancanti"; return false; }
            if (shops < 1 || libs < 1) { why = "negozio o biblioteca mancanti"; return false; }
            if (p.objectives.Count < 2) { why = "poche candele"; return false; }
            if (SurvivalLayout) return true; // sono marcatori di grandi sale, non zone da sigillare nel nuovo ciclo.
            var regions = RoundRegions.Find(p);
            if (regions.Count != p.objectives.Count) { why = $"candele senza zona sigillabile ({regions.Count}/{p.objectives.Count})"; return false; }
            return true;
        }
    }
}
