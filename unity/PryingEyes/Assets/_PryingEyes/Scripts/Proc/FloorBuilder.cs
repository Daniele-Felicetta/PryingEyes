using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Un piano costruito: radice, mappe delle stanze e punti utili a chi gioca (nascite dei nemici, ascensori).</summary>
    public sealed class FloorInstance
    {
        public Plan plan;
        public GameObject root;
        public Bounds bounds;
        public GameObject[] roomGo;
        public readonly List<Renderer> ceilings = new List<Renderer>();
        public readonly List<Vector3> groundSpawns = new List<Vector3>();
        public readonly List<Vector3> airSpawns = new List<Vector3>();
        public ProcLift arrival, exit;
        public readonly List<Candelabro> candles = new List<Candelabro>();
        public readonly List<Light> lights = new List<Light>();
        /// <summary>Radici degli arredi: la navmesh li tratta da ostacoli non camminabili (la loro cima non e' un'isola).</summary>
        public readonly List<Transform> propRoots = new List<Transform>();

        public Room RoomAt(Vector3 p)
        {
            foreach (var r in plan.rooms)
                if (p.x >= r.x0 * 0.1f && p.x <= r.x1 * 0.1f && p.z >= r.z0 * 0.1f && p.z <= r.z1 * 0.1f) return r;
            return null;
        }

        /// <summary>Altezza del soffitto sopra il punto (per chi vola).</summary>
        public float CeilingAt(Vector3 p)
        {
            var r = RoomAt(p);
            if (r == null) return 3f;
            return FloorBuilder.CeilHeight(r, p.x, p.z);
        }
    }

    /// <summary>Costruisce un piano dalla pianta: pavimenti, muri con porte, arcate e finestre, soffitti (piani, a travi,
    /// capriate, volte a botte), travi e nervature. Tutto in metri, una mesh per stanza e materiale. I muri sono sottili
    /// 30 cm: ogni stanza disegna la sua faccia, 15 cm dentro la linea, e tra le due facce resta il pieno.</summary>
    public static class FloorBuilder
    {
        public const float TH = 0.15f;          // mezzo spessore dei muri
        public const float Bay = 4.5f;          // passo delle campate

        static float M(int dm) => dm * 0.1f;

        public struct Style
        {
            public Material floor, wall, dado, ceil, trim, outer, beam;
            public float dadoH;
        }

        public static FloorInstance Build(Plan plan, Transform parent = null)
        {
            var fi = new FloorInstance { plan = plan };
            fi.root = new GameObject(plan.name);
            if (parent != null) fi.root.transform.SetParent(parent, false);
            fi.roomGo = new GameObject[plan.rooms.Count];
            foreach (var r in plan.rooms) BuildRoom(fi, r);
            plan.ComputeBounds();
            var b = new Bounds(new Vector3(M(plan.minX + plan.maxX) * 0.5f, 6f, M(plan.minZ + plan.maxZ) * 0.5f),
                               new Vector3(M(plan.maxX - plan.minX) + 8f, 16f, M(plan.maxZ - plan.minZ) + 8f));
            fi.bounds = b;
            return fi;
        }

        /// <summary>Superficie del pavimento per i passi: 0 pietra/marmo/piastrelle, 1 moquette, 2 legno.</summary>
        public static int Surface(Room r)
        {
            switch (r.kind)
            {
                case RoomKind.Corridoio: return 1;
                case RoomKind.Camera:
                case RoomKind.Suite: return r.variant % 3 == 0 ? 1 : 2;
                case RoomKind.Ristorante:
                case RoomKind.Sacrestia: return 2;
                default: return 0;
            }
        }

        // ------------------------------------------------------------------ stili
        public static Style StyleOf(Room r)
        {
            var s = new Style();
            int v = r.variant;
            string[] damask = { "mur_damasco", "mur_damasco_verde", "mur_damasco_ocra" };
            switch (r.kind)
            {
                case RoomKind.Atrio:
                    s.floor = Mat("pav_marmo"); s.wall = Mat(damask[0]); s.dado = Mat("mur_boiserie"); s.dadoH = 1.3f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); s.beam = Mat("trave"); break;
                case RoomKind.Corridoio:
                    s.floor = Mat("pav_moquette"); s.wall = Mat(damask[v % 3]); s.dado = Mat("mur_boiserie"); s.dadoH = 1.15f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); break;
                case RoomKind.Camera:
                case RoomKind.Suite:
                    s.floor = Mat(v % 3 == 0 ? "pav_moquette" : "pav_parquet"); s.wall = Mat(damask[(v / 3) % 3]); s.dado = Mat("mur_boiserie"); s.dadoH = 1.0f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); break;
                case RoomKind.Ristorante:
                case RoomKind.SalaBallo:
                    s.floor = Mat(r.kind == RoomKind.SalaBallo ? "pav_marmo" : "pav_parquet"); s.wall = Mat(damask[(v / 3) % 3]); s.dado = Mat("mur_boiserie"); s.dadoH = 1.3f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); s.beam = Mat("trave"); break;
                case RoomKind.Cucina:
                    s.floor = Mat("pav_piastrelle"); s.wall = Mat("mur_intonaco"); s.dado = Mat("mur_piastrelle"); s.dadoH = 1.9f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("mur_intonaco"); break;
                case RoomKind.Servizio:
                    s.floor = Mat("pav_servizio"); s.wall = Mat("mur_intonaco"); s.ceil = Mat("sof_intonaco"); s.trim = Mat("mur_intonaco"); break;
                case RoomKind.Nartece:
                case RoomKind.Navatella:
                case RoomKind.Campanile:
                    s.floor = Mat("pav_lastre"); s.wall = Mat("mur_pietra"); s.ceil = Mat("sof_legno"); s.trim = Mat("cornice_pietra"); s.beam = Mat("trave"); break;
                case RoomKind.Navata:
                    s.floor = Mat("pav_lastre"); s.wall = Mat("mur_pietra"); s.ceil = Mat("sof_legno"); s.trim = Mat("cornice_pietra"); s.beam = Mat("trave"); break;
                case RoomKind.Cappella:
                case RoomKind.Presbiterio:
                    s.floor = Mat(r.kind == RoomKind.Presbiterio ? "pav_marmo" : "pav_lastre"); s.wall = Mat("mur_pietra"); s.ceil = Mat("sof_volta"); s.trim = Mat("cornice_pietra"); s.beam = Mat("cornice_pietra"); break;
                case RoomKind.Sacrestia:
                    s.floor = Mat("pav_parquet"); s.wall = Mat("mur_intonaco"); s.dado = Mat("mur_boiserie"); s.dadoH = 1.4f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); s.beam = Mat("trave"); break;
                case RoomKind.Cripta:
                    s.floor = Mat("pav_cripta"); s.wall = Mat("mur_cripta"); s.ceil = Mat("sof_volta"); s.trim = Mat("cornice_pietra"); s.beam = Mat("cornice_pietra"); break;
                case RoomKind.Ascensore:
                    s.floor = Mat("pav_servizio"); s.wall = Mat("mur_boiserie"); s.ceil = Mat("mur_boiserie"); s.trim = Mat("ottone"); break;
            }
            s.outer = r.church ? Mat("mur_pietra") : Mat("mur_intonaco");
            return s;
        }

        static Material Mat(string n) => ProcKit.Mat(n);

        // ------------------------------------------------------------------ una stanza
        static void BuildRoom(FloorInstance fi, Room r)
        {
            var plan = fi.plan;
            var st = StyleOf(r);
            var go = new GameObject(string.Format("{0:00}_{1}", r.id, r.kind));
            go.transform.SetParent(fi.root.transform, false);
            fi.roomGo[r.id] = go;

            float x0 = M(r.x0), x1 = M(r.x1), z0 = M(r.z0), z1 = M(r.z1);
            float ix0 = x0 + TH, ix1 = x1 - TH, iz0 = z0 + TH, iz1 = z1 - TH;
            var shell = new MeshBuilder();
            var roof = new MeshBuilder();

            // pavimento: sotto i muri fino alla linea di mezzo, cosi' alle soglie non resta nessun buco
            shell.Quad(st.floor, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), new Vector3(x1, 0, z0),
                       new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0), Vector3.up);

            Ceiling(roof, r, st, ix0, ix1, iz0, iz1);

            // muri
            foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E }) WallSide(shell, plan, r, st, s, ix0, ix1, iz0, iz1);
            // riquadri delle aperture (spalle, davanzale, architrave) e vetri
            foreach (var o in plan.openings) if (o.a == r.id) OpeningGeometry(shell, plan, r, o, st);

            Flush(go, "Guscio", shell, true, null);
            Flush(go, "Soffitto", roof, true, fi.ceilings);

            // il collider del pavimento e' un blocco: niente caduta tra i triangoli
            var floorGo = new GameObject("Pavimento");
            floorGo.transform.SetParent(go.transform, false);
            var bc = floorGo.AddComponent<BoxCollider>();
            bc.center = new Vector3((x0 + x1) * 0.5f, -0.25f, (z0 + z1) * 0.5f);
            bc.size = new Vector3(x1 - x0, 0.5f, z1 - z0);

            RoomKits.Dress(fi, r, go.transform);
        }

        static void Flush(GameObject parent, string name, MeshBuilder mb, bool collide, List<Renderer> track)
        {
            if (mb.Empty) return;
            var mesh = mb.ToMesh(parent.name + "_" + name, out var mats);
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            if (collide) g.AddComponent<MeshCollider>().sharedMesh = mesh;
            track?.Add(mr);
        }

        // ------------------------------------------------------------------ altezza e soffitti
        public static float CeilHeight(Room r, float x, float z)
        {
            float x0 = M(r.x0) + TH, x1 = M(r.x1) - TH, z0 = M(r.z0) + TH, z1 = M(r.z1) - TH;
            if (r.ceil == CeilKind.Gable)
            {
                float a = r.ridgeAlongX ? (z1 - z0) * 0.5f : (x1 - x0) * 0.5f;
                float d = r.ridgeAlongX ? Mathf.Abs(z - (z0 + z1) * 0.5f) : Mathf.Abs(x - (x0 + x1) * 0.5f);
                return r.wallH + (r.ridgeH - r.wallH) * Mathf.Clamp01(1f - d / a);
            }
            if (r.ceil == CeilKind.BarrelRound || r.ceil == CeilKind.BarrelPointed)
            {
                float a = r.ridgeAlongX ? (z1 - z0) * 0.5f : (x1 - x0) * 0.5f;
                float d = r.ridgeAlongX ? z - (z0 + z1) * 0.5f : x - (x0 + x1) * 0.5f;
                return r.wallH + Arch.Y(d, a, r.ridgeH - r.wallH, r.ceil == CeilKind.BarrelPointed);
            }
            return r.wallH;
        }

        static void Ceiling(MeshBuilder mb, Room r, Style st, float ix0, float ix1, float iz0, float iz1)
        {
            float H = r.wallH;
            switch (r.ceil)
            {
                case CeilKind.Flat:
                case CeilKind.Beams:
                    mb.Quad(st.ceil, new Vector3(ix0, H, iz0), new Vector3(ix1, H, iz0), new Vector3(ix1, H, iz1), new Vector3(ix0, H, iz1),
                            new Vector2(ix0, iz0), new Vector2(ix1, iz0), new Vector2(ix1, iz1), new Vector2(ix0, iz1), Vector3.down);
                    if (r.ceil == CeilKind.Beams && st.beam != null) FlatBeams(mb, st, ix0, ix1, iz0, iz1, H);
                    break;
                case CeilKind.Gable:
                    GableCeiling(mb, r, st, ix0, ix1, iz0, iz1);
                    break;
                case CeilKind.BarrelRound:
                case CeilKind.BarrelPointed:
                    BarrelCeiling(mb, r, st, ix0, ix1, iz0, iz1);
                    break;
            }
        }

        static void FlatBeams(MeshBuilder mb, Style st, float ix0, float ix1, float iz0, float iz1, float H)
        {
            bool alongZ = (iz1 - iz0) <= (ix1 - ix0);   // le travi attraversano il lato corto
            float len = alongZ ? ix1 - ix0 : iz1 - iz0;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.4f));
            for (int k = 0; k < n; k++)
            {
                float p = (k + 0.5f) / n * len;
                if (alongZ) mb.Beam(st.beam, new Vector3(ix0 + p, H - 0.15f, iz0), new Vector3(ix0 + p, H - 0.15f, iz1), 0.24f, 0.30f, Vector3.up);
                else mb.Beam(st.beam, new Vector3(ix0, H - 0.15f, iz0 + p), new Vector3(ix1, H - 0.15f, iz0 + p), 0.24f, 0.30f, Vector3.up);
            }
        }

        static void GableCeiling(MeshBuilder mb, Room r, Style st, float ix0, float ix1, float iz0, float iz1)
        {
            float H = r.wallH, R = r.ridgeH;
            if (r.ridgeAlongX)
            {
                float zc = (iz0 + iz1) * 0.5f;
                float sl = Mathf.Sqrt((zc - iz0) * (zc - iz0) + (R - H) * (R - H));
                mb.Quad(st.ceil, new Vector3(ix0, H, iz0), new Vector3(ix1, H, iz0), new Vector3(ix1, R, zc), new Vector3(ix0, R, zc),
                        new Vector2(ix0, 0), new Vector2(ix1, 0), new Vector2(ix1, sl), new Vector2(ix0, sl), Vector3.down);
                mb.Quad(st.ceil, new Vector3(ix0, H, iz1), new Vector3(ix1, H, iz1), new Vector3(ix1, R, zc), new Vector3(ix0, R, zc),
                        new Vector2(ix0, 0), new Vector2(ix1, 0), new Vector2(ix1, sl), new Vector2(ix0, sl), Vector3.down);
                if (st.beam == null) return;
                // capriate a ogni campata: catena, puntoni, monaco e due saette; correnti lungo il tetto
                int bays = Mathf.Max(1, Mathf.RoundToInt((ix1 - ix0) / Bay));
                for (int k = 1; k < bays; k++)
                {
                    float x = ix0 + (ix1 - ix0) * k / bays;
                    Truss(mb, st.beam, new Vector3(x, 0, iz0), new Vector3(x, 0, iz1), new Vector3(x, 0, zc), H, R);
                }
                mb.Beam(st.beam, new Vector3(ix0, R - 0.14f, zc), new Vector3(ix1, R - 0.14f, zc), 0.30f, 0.32f, Vector3.up);
                foreach (float f in new[] { 0.5f })
                    foreach (float zz in new[] { Mathf.Lerp(zc, iz0, f), Mathf.Lerp(zc, iz1, f) })
                        mb.Beam(st.beam, new Vector3(ix0, Mathf.Lerp(R, H, f) - 0.12f, zz), new Vector3(ix1, Mathf.Lerp(R, H, f) - 0.12f, zz), 0.2f, 0.22f, Vector3.up);
            }
            else
            {
                float xc = (ix0 + ix1) * 0.5f;
                float sl = Mathf.Sqrt((xc - ix0) * (xc - ix0) + (R - H) * (R - H));
                mb.Quad(st.ceil, new Vector3(ix0, H, iz0), new Vector3(ix0, H, iz1), new Vector3(xc, R, iz1), new Vector3(xc, R, iz0),
                        new Vector2(iz0, 0), new Vector2(iz1, 0), new Vector2(iz1, sl), new Vector2(iz0, sl), Vector3.down);
                mb.Quad(st.ceil, new Vector3(ix1, H, iz0), new Vector3(ix1, H, iz1), new Vector3(xc, R, iz1), new Vector3(xc, R, iz0),
                        new Vector2(iz0, 0), new Vector2(iz1, 0), new Vector2(iz1, sl), new Vector2(iz0, sl), Vector3.down);
                if (st.beam == null) return;
                int bays = Mathf.Max(1, Mathf.RoundToInt((iz1 - iz0) / Bay));
                for (int k = 1; k < bays; k++)
                {
                    float z = iz0 + (iz1 - iz0) * k / bays;
                    Truss(mb, st.beam, new Vector3(ix0, 0, z), new Vector3(ix1, 0, z), new Vector3(xc, 0, z), H, R);
                }
                mb.Beam(st.beam, new Vector3(xc, R - 0.14f, iz0), new Vector3(xc, R - 0.14f, iz1), 0.30f, 0.32f, Vector3.up);
            }
        }

        // capriata: a = appoggio 1, b = appoggio 2, c = colmo (ordinate in y ignorate: H = quota d'imposta, R = colmo)
        static void Truss(MeshBuilder mb, Material m, Vector3 a, Vector3 b, Vector3 c, float H, float R)
        {
            Vector3 A = new Vector3(a.x, H, a.z), B = new Vector3(b.x, H, b.z), C = new Vector3(c.x, R, c.z);
            Vector3 T0 = new Vector3(a.x, H - 0.18f, a.z), T1 = new Vector3(b.x, H - 0.18f, b.z);
            mb.Beam(m, T0, T1, 0.32f, 0.36f, Vector3.up);                                   // catena
            mb.Beam(m, A + Vector3.down * 0.16f, C + Vector3.down * 0.16f, 0.28f, 0.30f, Vector3.up);   // puntoni
            mb.Beam(m, B + Vector3.down * 0.16f, C + Vector3.down * 0.16f, 0.28f, 0.30f, Vector3.up);
            Vector3 mid = new Vector3(c.x, H - 0.18f, c.z);
            mb.Beam(m, mid, C + Vector3.down * 0.16f, 0.26f, 0.26f, Vector3.right);          // monaco
            Vector3 q1 = Vector3.Lerp(T0, T1, 0.25f), q2 = Vector3.Lerp(T0, T1, 0.75f);
            Vector3 r1 = Vector3.Lerp(A, C, 0.55f) + Vector3.down * 0.16f, r2 = Vector3.Lerp(B, C, 0.55f) + Vector3.down * 0.16f;
            mb.Beam(m, q1, r1, 0.2f, 0.2f, Vector3.right);                                  // saette
            mb.Beam(m, q2, r2, 0.2f, 0.2f, Vector3.right);
        }

        static void BarrelCeiling(MeshBuilder mb, Room r, Style st, float ix0, float ix1, float iz0, float iz1)
        {
            float H = r.wallH, rise = r.ridgeH - r.wallH;
            bool pointed = r.ceil == CeilKind.BarrelPointed;
            bool alongX = r.ridgeAlongX;
            float a = alongX ? (iz1 - iz0) * 0.5f : (ix1 - ix0) * 0.5f;
            float c = alongX ? (iz0 + iz1) * 0.5f : (ix0 + ix1) * 0.5f;
            float a0 = alongX ? ix0 : iz0, a1 = alongX ? ix1 : iz1;      // estremi lungo l'asse della volta
            int n = 18;
            // profilo in sezione: (distanza dall'asse, quota)
            var px = new float[n + 1]; var py = new float[n + 1]; var nrm = new Vector2[n + 1]; var arc = new float[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = -a + 2f * a * i / n;
                px[i] = t; py[i] = H + Arch.Y(t, a, rise, pointed); nrm[i] = Arch.InNormal(t, a, rise, pointed);
                arc[i] = i == 0 ? 0f : arc[i - 1] + Mathf.Sqrt((px[i] - px[i - 1]) * (px[i] - px[i - 1]) + (py[i] - py[i - 1]) * (py[i] - py[i - 1]));
            }
            Vector3 Pt(float along, int i, float offs = 0f)
            {
                float dd = px[i] + nrm[i].x * offs, yy = py[i] + nrm[i].y * offs;
                return alongX ? new Vector3(along, yy, c + dd) : new Vector3(c + dd, yy, along);
            }
            Vector3 Nrm(int i) => alongX ? new Vector3(0f, nrm[i].y, nrm[i].x) : new Vector3(nrm[i].x, nrm[i].y, 0f);
            for (int i = 0; i < n; i++)
                mb.QuadSmooth(st.ceil, Pt(a0, i), Pt(a1, i), Pt(a1, i + 1), Pt(a0, i + 1), Nrm(i), Nrm(i), Nrm(i + 1), Nrm(i + 1),
                              new Vector2(a0, arc[i]), new Vector2(a1, arc[i]), new Vector2(a1, arc[i + 1]), new Vector2(a0, arc[i + 1]));
            // nervature ogni ~3 m
            if (st.beam == null) return;
            float len = a1 - a0;
            int ribs = Mathf.Max(1, Mathf.RoundToInt(len / 3.2f));
            const float w = 0.16f, dep = 0.16f;
            for (int k = 0; k <= ribs; k++)
            {
                float p = a0 + len * k / ribs;
                for (int i = 0; i < n; i++)
                {
                    // fascia sotto: tra i due profili spostati verso l'interno
                    mb.QuadSmooth(st.beam, Pt(p - w, i, dep), Pt(p + w, i, dep), Pt(p + w, i + 1, dep), Pt(p - w, i + 1, dep), Nrm(i), Nrm(i), Nrm(i + 1), Nrm(i + 1),
                                  new Vector2(0, arc[i]), new Vector2(0.32f, arc[i]), new Vector2(0.32f, arc[i + 1]), new Vector2(0, arc[i + 1]));
                    // fianchi della nervatura
                    foreach (float sg in new[] { -1f, 1f })
                    {
                        Vector3 s0 = Pt(p + sg * w, i, 0f), s1 = Pt(p + sg * w, i + 1, 0f), d0 = Pt(p + sg * w, i, dep), d1 = Pt(p + sg * w, i + 1, dep);
                        Vector3 axis = alongX ? new Vector3(sg, 0f, 0f) : new Vector3(0f, 0f, sg);
                        mb.Quad(st.beam, s0, s1, d1, d0, new Vector2(0, arc[i]), new Vector2(0, arc[i + 1]), new Vector2(dep, arc[i + 1]), new Vector2(dep, arc[i]), axis);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ muri
        static void WallSide(MeshBuilder mb, Plan plan, Room r, Style st, Side s, float ix0, float ix1, float iz0, float iz1)
        {
            bool ax = Plan.AlongX(s);
            Vector3 origin, u, facing;
            float len;
            switch (s)
            {
                case Side.S: origin = new Vector3(ix0, 0, iz0); u = Vector3.right; facing = Vector3.forward; len = ix1 - ix0; break;
                case Side.N: origin = new Vector3(ix0, 0, iz1); u = Vector3.right; facing = Vector3.back; len = ix1 - ix0; break;
                case Side.W: origin = new Vector3(ix0, 0, iz0); u = Vector3.forward; facing = Vector3.right; len = iz1 - iz0; break;
                default: origin = new Vector3(ix1, 0, iz0); u = Vector3.forward; facing = Vector3.left; len = iz1 - iz0; break;
            }
            float faceStart = ax ? ix0 : iz0;
            TopFunc(r, ax, len, out var top, out var breaks);

            var holes = new List<Hole>();
            foreach (var o in plan.OpeningsOn(r, s))
                holes.Add(new Hole { c = M(o.center) - faceStart, hw = o.width * 0.05f, o = o });

            DrawFace(mb, st.wall, st.dado, st.dadoH, origin, u, facing, len, top, breaks, holes, faceStart);

            // pelle esterna lungo i tratti che danno sul vuoto
            Vector3 outward = -facing;
            foreach (var seg in plan.Segments(r, s))
            {
                if (seg.other >= 0) continue;
                float sa = M(seg.a0), sb = M(seg.a1);
                Vector3 o2 = ax ? new Vector3(sa, 0, Plan.Line(r, s) * 0.1f) : new Vector3(Plan.Line(r, s) * 0.1f, 0, sa);
                o2 += outward * TH;
                var hs = new List<Hole>();
                foreach (var h in holes)
                {
                    float c = h.c + faceStart - sa;
                    if (h.o.b < 0 && c - h.hw >= -0.001f && c + h.hw <= sb - sa + 0.001f && !h.o.oneSided) hs.Add(new Hole { c = c, hw = h.hw, o = h.o });
                }
                float shift = sa - faceStart;
                TopFunc(r, ax, len, out var top0, out var br0);
                Func<float, float> topShifted = x => top0(x + shift);
                DrawFace(mb, st.outer, null, 0f, o2, u, outward, sb - sa, topShifted, null, hs, sa);
            }
        }

        static void DrawFace(MeshBuilder mb, Material wall, Material dado, float dadoH, Vector3 origin, Vector3 u, Vector3 facing, float len,
                             Func<float, float> top, float[] breaks, List<Hole> holes, float uOff)
        {
            if (len < 0.01f) return;
            if (dado != null)
            {
                mb.WallFace(dado, origin, u, facing, len, 0f, x => Mathf.Min(dadoH, top(x)), holes, uOff, breaks);
                mb.WallFace(wall, origin, u, facing, len, dadoH, top, holes, uOff, breaks);
            }
            else mb.WallFace(wall, origin, u, facing, len, 0f, top, holes, uOff, breaks);
        }

        /// <summary>Profilo alto del muro lungo una faccia: costante, oppure triangolo/arco sulle testate delle capriate e delle volte.</summary>
        static void TopFunc(Room r, bool faceAlongX, float len, out Func<float, float> top, out float[] breaks)
        {
            float H = r.wallH;
            breaks = null;
            top = x => H;
            bool parallel = r.ridgeAlongX == faceAlongX;
            if (parallel) return;
            if (r.ceil == CeilKind.Gable)
            {
                float R = r.ridgeH - H, half = len * 0.5f;
                top = x => H + R * Mathf.Clamp01(1f - Mathf.Abs(x - half) / half);
                breaks = new[] { half };
            }
            else if (r.ceil == CeilKind.BarrelRound || r.ceil == CeilKind.BarrelPointed)
            {
                float rise = r.ridgeH - H, a = len * 0.5f;
                bool pt = r.ceil == CeilKind.BarrelPointed;
                top = x => H + Arch.Y(x - a, a, rise, pt);
                int n = 28;
                breaks = new float[n - 1];
                for (int i = 1; i < n; i++) breaks[i - 1] = len * i / n;
            }
        }

        // ------------------------------------------------------------------ aperture
        static void OpeningGeometry(MeshBuilder mb, Plan plan, Room A, Opening o, Style st)
        {
            bool ax = o.alongX;
            Vector3 e = ax ? Vector3.right : Vector3.forward;
            Vector3 nOut = ax ? (A.z1 == o.line ? Vector3.forward : Vector3.back) : (A.x1 == o.line ? Vector3.right : Vector3.left);
            float line = M(o.line);
            float sLo = M(o.Lo), sHi = M(o.Hi), hw = (sHi - sLo) * 0.5f, c = (sLo + sHi) * 0.5f;
            float dA = -TH, dB = TH;
            Vector3 P(float s, float y, float d) => ax ? new Vector3(s, y, line + d * nOut.z) : new Vector3(line + d * nOut.x, y, s);
            var mat = st.trim != null ? st.trim : st.wall;
            float edgeTop = o.Pointed ? o.top - o.RiseFactor * hw : o.top;
            float sill = o.sill;
            // spalle
            mb.Quad(mat, P(sLo, sill, dA), P(sLo, edgeTop, dA), P(sLo, edgeTop, dB), P(sLo, sill, dB),
                    new Vector2(0, sill), new Vector2(0, edgeTop), new Vector2(0.3f, edgeTop), new Vector2(0.3f, sill), e);
            mb.Quad(mat, P(sHi, sill, dA), P(sHi, edgeTop, dA), P(sHi, edgeTop, dB), P(sHi, sill, dB),
                    new Vector2(0, sill), new Vector2(0, edgeTop), new Vector2(0.3f, edgeTop), new Vector2(0.3f, sill), -e);
            if (sill > 0.01f)
                mb.Quad(mat, P(sLo, sill, dA), P(sHi, sill, dA), P(sHi, sill, dB), P(sLo, sill, dB),
                        new Vector2(sLo, 0), new Vector2(sHi, 0), new Vector2(sHi, 0.3f), new Vector2(sLo, 0.3f), Vector3.up);
            if (!o.Pointed)
                mb.Quad(mat, P(sLo, o.top, dA), P(sHi, o.top, dA), P(sHi, o.top, dB), P(sLo, o.top, dB),
                        new Vector2(sLo, 0), new Vector2(sHi, 0), new Vector2(sHi, 0.3f), new Vector2(sLo, 0.3f), Vector3.down);
            else
            {
                int n = Mathf.Max(8, Mathf.CeilToInt(2f * hw / 0.12f));
                float rise = o.RiseFactor * hw;
                float acc = 0f;
                float prevY = 0f;
                for (int i = 0; i < n; i++)
                {
                    float s0 = sLo + 2f * hw * i / n, s1 = sLo + 2f * hw * (i + 1) / n;
                    float y0 = o.Head(s0 - c, hw), y1 = o.Head(s1 - c, hw);
                    var n0 = ArchNormal(o, s0 - c, hw, rise, e); var n1 = ArchNormal(o, s1 - c, hw, rise, e);
                    float seg = Mathf.Sqrt((s1 - s0) * (s1 - s0) + (y1 - y0) * (y1 - y0));
                    mb.QuadSmooth(mat, P(s0, y0, dA), P(s1, y1, dA), P(s1, y1, dB), P(s0, y0, dB), n0, n1, n1, n0,
                                  new Vector2(acc, 0), new Vector2(acc + seg, 0), new Vector2(acc + seg, 0.3f), new Vector2(acc, 0.3f));
                    acc += seg; prevY = y1;
                }
            }
            // vetro al centro dello spessore (solo le finestre sul vuoto)
            if (o.b < 0) Glass(mb, o, A, P, nOut);
        }

        /// <summary>Normale verso l'interno dell'apertura (in basso e verso l'asse): ogni meta' dell'arco acuto ha il centro
        /// sull'altro lato dell'asse, a distanza d, e la superficie guarda il suo centro.</summary>
        static Vector3 ArchNormal(Opening o, float dx, float hw, float rise, Vector3 e)
        {
            float d = (rise * rise - hw * hw) / (2f * hw);
            float R = hw + d;
            float xx = Mathf.Abs(dx) + d;
            float yy = Mathf.Sqrt(Mathf.Max(0.0001f, R * R - xx * xx));
            float sgn = dx >= 0f ? 1f : -1f;
            return (e * (-sgn * xx / R) + Vector3.up * (-yy / R)).normalized;
        }

        static void Glass(MeshBuilder mb, Opening o, Room A, Func<float, float, float, Vector3> P, Vector3 nOut)
        {
            var mat = o.style == OpenStyle.Lancetta ? Mat("vetrata" + (o.variant & 3)) : o.style == OpenStyle.Feritoia ? Mat("vetro_buio") : Mat("vetro_albergo");
            Vector3 facing = -nOut;
            float sLo = M(o.Lo), sHi = M(o.Hi), hw = (sHi - sLo) * 0.5f, c = (sLo + sHi) * 0.5f;
            float w = sHi - sLo, hgt = o.top - o.sill;
            if (!o.Pointed)
            {
                mb.Quad(mat, P(sLo, o.sill, 0f), P(sHi, o.sill, 0f), P(sHi, o.top, 0f), P(sLo, o.top, 0f),
                        new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), facing);
                return;
            }
            int n = Mathf.Max(8, Mathf.CeilToInt(2f * hw / 0.1f));
            for (int i = 0; i < n; i++)
            {
                float s0 = sLo + w * i / n, s1 = sLo + w * (i + 1) / n;
                float y0 = o.Head(s0 - c, hw), y1 = o.Head(s1 - c, hw);
                mb.Quad(mat, P(s0, o.sill, 0f), P(s1, o.sill, 0f), P(s1, y1, 0f), P(s0, y0, 0f),
                        new Vector2((s0 - sLo) / w, 0), new Vector2((s1 - sLo) / w, 0), new Vector2((s1 - sLo) / w, (y1 - o.sill) / hgt), new Vector2((s0 - sLo) / w, (y0 - o.sill) / hgt), facing);
            }
        }
    }
}
