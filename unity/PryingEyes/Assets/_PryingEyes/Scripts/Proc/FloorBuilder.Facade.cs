using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>L'esterno degli edifici del deserto. Le stanze dei piani sono pensate per chi sta dentro: i soffitti guardano solo in basso (da fuori non si vedono: niente tetto), i muri
    /// guardano solo dentro e dove una stanza alta confina con una bassa (la navata e la navatella, l'atrio e le camere) la parte di muro sopra il tetto vicino non ha nessuna
    /// faccia verso fuori. Qui si disegna quello che manca: i tetti (piani con il parapetto, a falde, a volta, la guglia del campanile), i muri sopra i tetti piu' bassi, lo
    /// zoccolo, la cornice, le lesene e i contrafforti, i riquadri delle finestre con le persiane, finestre finte a passo regolare (alcune accese di notte), le soglie, i sassi
    /// dei crolli e i merli delle torri. Con 'farSkin' disegna anche la pelle dei muri, piena e con le finestre dipinte: e' la facciata di chi sta lontano (il guscio con le sue
    /// pareti vere si costruisce solo da vicino, Deserto/SiteFacade.cs).</summary>
    public static partial class FloorBuilder
    {
        static bool exteriorBuild;
#if !PE_DISTRIBUTION_BUILD
        /// <summary>Solo per la prova dei muri del deserto (DesertCensus): costruisce i muri come nella 0.9.0, senza i profili sulla pelle esterna, senza il taglio delle fette dove il profilo
        /// scende sotto un'apertura e senza il bordo basso a denti dei muri sopra un vicino crollato, per misurare prima e dopo la stessa pianta.</summary>
        public static bool LegacyWalls;
#else
        public const bool LegacyWalls = false;
#endif
        /// <summary>Il deserto costruisce l'esterno dei luoghi a parte (SiteFacade): Build non lo fa per loro.</summary>
        public static bool SkipFacade;
        /// <summary>La tavolozza dei muri del luogo che si sta costruendo (0..63): vedi FacadeKit.For.</summary>
        public static int FacadePalette;
        /// <summary>Il piano che si costruisce ha i tetti (l'ultimo livello di una torre, un edificio) e i merli sul parapetto.</summary>
        public static bool FacadeRoofs = true, FacadeCrown;
        /// <summary>Il piano che si costruisce e' l'ultimo di una torre: ai suoi angoli in fuori il tetto ha una torretta con la punta a piramide (Turrets).</summary>
        public static bool FacadeTurrets;
        /// <summary>Quanto la pelle di chi sta lontano sporge dal muro vero (m).</summary>
        public const float FarInflate = 0.04f;
        /// <summary>Il piano che si costruisce e' al livello del suolo (un edificio, il piano terra di una torre, l'ingresso di una catacomba): i suoi muri hanno la sabbia accumulata alla base (Drift).</summary>
        public static bool FacadeDrift;

        static float Sm(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>La sabbia che il vento ha accumulato contro il muro: una rampa irregolare lungo tutto il tratto (un po' oltre le due estremita', cosi' agli spigoli si arrotonda), alta fino a mezzo metro
        /// e larga fino a due metri e mezzo, con l'altezza e la larghezza che cambiano piano lungo il muro (rumore in coordinate del mondo: due tratti che si toccano si incontrano alla stessa quota).
        /// Davanti alle porte si abbassa a zero. Stesso shader e stessi colori del terreno (la tinta della regione e' nei colori dei vertici), quindi il muro sembra affondare nella duna invece di
        /// posare su un piano. Solo da vedere: nessun collider.</summary>
        static void Drift(MeshBuilder mb, in Run w, float len, List<Vector2> doors)
        {
            var sand = FacadeKit.Sand();
            if (sand == null || len < 0.8f) return;
            const float ext = 0.7f;
            float total = len + 2f * ext;
            int n = Mathf.Max(4, Mathf.CeilToInt(total / 0.55f));
            var P = new Vector3[n + 1, 4]; var N = new Vector3[n + 1, 4]; var C = new Color32[n + 1, 4];
            var dy = new float[4]; var dd = new float[4];
            for (int i = 0; i <= n; i++)
            {
                float x = -ext + total * i / n;
                Vector3 b = w.P(x, 0f, 0f);
                float n1 = Mathf.PerlinNoise(b.x * 0.23f + 11.3f, b.z * 0.23f + 5.1f);
                float n2 = Mathf.PerlinNoise(b.x * 0.61f + 2.7f, b.z * 0.61f + 9.9f);
                float n3 = Mathf.PerlinNoise(b.x * 0.17f + 31f, b.z * 0.17f - 7f);
                float h = 0.08f + 0.50f * Sm(0.30f, 0.78f, n1) * (0.65f + 0.35f * n2);
                float wd = 0.50f + 1.9f * Sm(0.22f, 0.85f, n3) * (0.70f + 0.30f * n2);
                float endF = Sm(-ext, 0f, x) * Sm(len + ext, len, x);
                foreach (var d in doors)
                {
                    float dist = Mathf.Max(Mathf.Max(d.x - 0.25f - x, x - (d.y + 0.25f)), 0f);
                    float clear = Sm(0.15f, 1.3f, dist);
                    h *= clear; wd *= 0.35f + 0.65f * clear;
                }
                h *= endF; wd *= 0.4f + 0.6f * endF;
                // dal muro verso fuori: il contatto, due punti intermedi, il bordo (appena sotto il terreno, perche' non si veda uno scalino)
                dd[0] = 0.01f; dy[0] = h;
                dd[1] = wd * 0.38f; dy[1] = h * 0.52f;
                dd[2] = wd * 0.72f; dy[2] = h * 0.14f;
                dd[3] = wd; dy[3] = -0.10f;
                for (int r = 0; r < 4; r++)
                {
                    var p = w.P(x, dy[r], dd[r]);
                    P[i, r] = p;
                    // la pendenza media dei due tratti che toccano il punto: la normale gira dolcemente
                    float s0 = r > 0 ? (dy[r - 1] - dy[r]) / Mathf.Max(0.05f, dd[r] - dd[r - 1]) : (dy[0] - dy[1]) / Mathf.Max(0.05f, dd[1] - dd[0]);
                    float s1 = r < 3 ? (dy[r] - dy[r + 1]) / Mathf.Max(0.05f, dd[r + 1] - dd[r]) : (dy[2] - dy[3]) / Mathf.Max(0.05f, dd[3] - dd[2]);
                    N[i, r] = (Vector3.up + w.n * Mathf.Clamp((s0 + s1) * 0.5f, 0f, 1.6f)).normalized;
                    Deserto.DesertWorld.TintAt(p.x, p.z, out float tr, out float tg, out float tb, out float td);
                    C[i, r] = new Color32((byte)Mathf.Clamp(tr * 127.5f, 0f, 255f), (byte)Mathf.Clamp(tg * 127.5f, 0f, 255f), (byte)Mathf.Clamp(tb * 127.5f, 0f, 255f), (byte)Mathf.Clamp(td * 255f, 0f, 255f));
                }
            }
            for (int i = 0; i < n; i++)
                for (int r = 0; r < 3; r++)
                    mb.QuadSmoothC(sand, P[i, r], P[i + 1, r], P[i + 1, r + 1], P[i, r + 1], N[i, r], N[i + 1, r], N[i + 1, r + 1], N[i, r + 1], C[i, r], C[i + 1, r], C[i + 1, r + 1], C[i, r + 1]);
        }

        /// <summary>Il profilo a denti di un muro crollato: punte e vuoti lungo il muro, piu' bassi fino a un metro e mezzo. Lo stesso per le due facce (dentro e fuori) e deciso dalla stanza e dal lato.</summary>
        static void Jag(Room r, float len, int salt, out Func<float, float> top, out float[] breaks)
        {
            var rng = new Rng(unchecked(r.variant * 7919 + r.id * 131 + salt * 17 + 3));
            float H = r.wallH, amp = Mathf.Min(1.7f, H * 0.5f);
            var xs = new List<float> { 0f }; var ys = new List<float> { H - amp * rng.Range(0f, 0.6f) };
            float x = 0f;
            while (x < len - 0.01f)
            {
                x += rng.Range(0.45f, 1.5f);
                if (x > len - 0.3f) x = len;
                xs.Add(x);
                ys.Add(H - (rng.Chance(0.16) ? amp : rng.Range(0f, 0.65f) * amp));
            }
            var X = xs.ToArray(); var Y = ys.ToArray();
            top = t =>
            {
                if (t <= 0f) return Y[0];
                for (int i = 1; i < X.Length; i++)
                    if (t <= X[i]) return Mathf.Lerp(Y[i - 1], Y[i], (t - X[i - 1]) / Mathf.Max(1e-4f, X[i] - X[i - 1]));
                return Y[Y.Length - 1];
            };
            if (X.Length <= 2) { breaks = null; return; }
            breaks = new float[X.Length - 2];
            for (int i = 1; i < X.Length - 1; i++) breaks[i - 1] = X[i];
        }

        struct Run
        {
            public Room r; public Side s;
            public Vector3 o, u, n;                 // origine sul piano della pelle esterna (alla quota del piano), lungo il muro, verso fuori
            public float len;
            public Func<float, float> top;
            public float[] breaks;
            public float faceStart, sa;
            public Vector3 P(float x, float y, float d) => o + u * x + Vector3.up * y + n * d;
        }

        /// <summary>I punti in cui il profilo di una faccia cambia pendenza, spostati nelle ascisse di un tratto che comincia 'shift' metri dopo l'inizio della faccia: solo quelli dentro il tratto (lungo 'len').</summary>
        static float[] ShiftBreaks(float[] breaks, float shift, float len)
        {
            if (breaks == null) return null;
            var l = new List<float>();
            foreach (var b in breaks) { float x = b - shift; if (x > 0.01f && x < len - 0.01f) l.Add(x); }
            return l.Count > 0 ? l.ToArray() : null;
        }

        static bool MakeRun(Room r, Side s, Seg seg, float y0, out Run run)
        {
            run = default;
            if (r.Open) return false;
            bool ax = Plan.AlongX(s);
            float ix0 = M(r.x0) + TH, ix1 = M(r.x1) - TH, iz0 = M(r.z0) + TH, iz1 = M(r.z1) - TH;
            float faceLen = ax ? ix1 - ix0 : iz1 - iz0, faceStart = ax ? ix0 : iz0;
            Vector3 facing = s == Side.S ? Vector3.forward : s == Side.N ? Vector3.back : s == Side.W ? Vector3.right : Vector3.left;
            Vector3 outward = -facing;
            float sa = M(seg.a0), sb = M(seg.a1);
            if (sb - sa < 0.05f) return false;
            Vector3 o2 = (ax ? new Vector3(sa, y0, Plan.Line(r, s) * 0.1f) : new Vector3(Plan.Line(r, s) * 0.1f, y0, sa)) + outward * TH;
            TopFunc(r, ax, faceLen, out var top0, out var br0, (int)s);
            float shift = sa - faceStart;
            float[] br = ShiftBreaks(br0, shift, sb - sa);
            run = new Run { r = r, s = s, o = o2, u = ax ? Vector3.right : Vector3.forward, n = outward, len = sb - sa, top = x => top0(x + shift), breaks = br, faceStart = faceStart, sa = sa };
            return true;
        }

        // ------------------------------------------------------------------ pezzi
        /// <summary>Un parallelepipedo rispetto al muro: da x0 a x1 lungo, da y0 a y1 in alto, da d0 a d1 verso fuori (misure dalla pelle).</summary>
        static void Blk(MeshBuilder mb, Material m, in Run w, float x0, float x1, float y0, float y1, float d0, float d1, bool bottom = true)
        {
            if (x1 - x0 < 0.005f || y1 - y0 < 0.005f || d1 - d0 < 0.005f) return;
            float yaw = Mathf.Atan2(-w.u.z, w.u.x) * Mathf.Rad2Deg;
            mb.Box(m, w.P((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (d0 + d1) * 0.5f), new Vector3(x1 - x0, y1 - y0, d1 - d0), yaw, true, bottom);
        }

        /// <summary>Un riquadro piatto sul muro (a distanza d dalla pelle), rivolto fuori, con UV in metri.</summary>
        static void Flat(MeshBuilder mb, Material m, in Run w, float x0, float x1, float y0, float y1, float d)
        {
            if (x1 - x0 < 0.005f || y1 - y0 < 0.005f) return;
            mb.Quad(m, w.P(x0, y0, d), w.P(x1, y0, d), w.P(x1, y1, d), w.P(x0, y1, d), new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1), w.n);
        }

        /// <summary>Dove comincia, verso dentro, un pezzo che sta nello spessore del muro (cornice, parapetto, merli): la sua faccia verso la stanza stava esattamente sulla faccia interna del muro (-2 x TH) e le
        /// due si contendevano i pixel nella fascia in cima al muro che hanno in comune; 6 mm piu' in fuori la faccia interna del muro le sta davanti e quelle sopra il muro restano come sono.</summary>
        static float InnerFlush => LegacyWalls ? -2f * TH : -2f * TH + 0.006f;

        static bool HasRoof(Room r) => !(r.ruined || r.noCeil || r.Open || r.IsLift || r.ceil == CeilKind.Open);

        static int EdgeStatus(Plan plan, Room r, Side s)
        {
            int ext = 0, tot = 0;
            foreach (var seg in plan.Segments(r, s)) { tot += seg.a1 - seg.a0; if (seg.other < 0) ext += seg.a1 - seg.a0; }
            return ext == 0 ? 0 : ext == tot ? 2 : 1;
        }

        static readonly Side[] Sides = { Side.S, Side.N, Side.W, Side.E };

        // ------------------------------------------------------------------ l'esterno di una pianta
        /// <summary>Disegna l'esterno di una pianta. 'y0' e' la quota del suo pavimento; 'roofs' mette i tetti (l'ultimo livello di una torre, tutti i piani di un edificio basso);
        /// 'crown' mette i merli sul parapetto; 'farSkin' disegna anche la pelle dei muri in 'skin'; 'detail' = false salta le rifiniture piccole (la facciata di chi e' lontano).</summary>
        internal static void FacadeBuild(Plan plan, float y0, int palette, bool roofs, bool crown, bool farSkin, bool detail, MeshBuilder dress, MeshBuilder skin, int from = 0, int to = int.MaxValue, bool turrets = false)
        {
            for (int ri = from; ri < plan.rooms.Count && ri < to; ri++)
            {
                var r = plan.rooms[ri];
                if (r.Open || r.IsLift) continue;
                var st = FacadeKit.For(plan.theme, r.church, palette);
                bool roofed = roofs && HasRoof(r);
                bool crumble = r.crumble && r.ruined;
                foreach (var s in Sides)
                {
                    foreach (var seg in plan.Segments(r, s))
                    {
                        if (!MakeRun(r, s, seg, y0, out var w)) continue;
                        if (seg.other < 0) ExteriorRun(plan, r, s, seg, w, st, roofed, crown, crumble, farSkin, detail, dress, skin, palette);
                        else StepRun(plan, r, s, seg, w, st, plan.rooms[seg.other], dress, detail);
                    }
                }
                if (roofed) AddRoof(plan, r, y0, st, crown, detail, dress, turrets);
            }
        }

        static void ExteriorRun(Plan plan, Room r, Side s, Seg seg, in Run w, FacadeStyle st, bool roofed, bool crown, bool crumble, bool farSkin, bool detail,
                                MeshBuilder dress, MeshBuilder skin, int palette)
        {
            var rng = new Rng(unchecked(r.seed * 131 + r.variant * 7 + (int)s * 977 + seg.a0 * 3 + 11));
            float len = w.len, topY = w.top(0f);
            bool flat = w.breaks == null && Mathf.Abs(w.top(0f) - w.top(len)) < 0.01f && !crumble;
            bool ax = Plan.AlongX(s);
            float sa = w.sa;
            // le aperture sul vuoto che stanno su questo tratto
            var doors = new List<Vector2>(); var wins = new List<Opening>(); var cuts = new List<Vector2>();
            var gaps = new List<Vector3>();         // le porte senza architrave (Opening.Gap): da, a, altezza della testa
            foreach (var o in plan.OpeningsOn(r, s))
            {
                if (o.b >= 0 || o.oneSided) continue;
                float c = M(o.center) - sa, hw = o.width * 0.05f;
                if (c - hw < -0.001f || c + hw > len + 0.001f) continue;
                cuts.Add(new Vector2(c - hw - 0.25f, c + hw + 0.25f));
                if (o.sill < 0.01f) { doors.Add(new Vector2(c - hw, c + hw)); if (o.Gap) gaps.Add(new Vector3(c - hw, c + hw, o.top)); }
                else wins.Add(o);
            }
            cuts.Sort((p, q) => p.x.CompareTo(q.x));
            doors.Sort((p, q) => p.x.CompareTo(q.x));

            // la pelle dei muri di chi sta lontano: piena, con le aperture dipinte
            if (farSkin && skin != null)
            {
                // un po' fuori dalla pelle vera: quando il guscio si costruisce (e questa pelle si spegne) i due non si sovrappongono mai a filo
                skin.WallFace(st.wall, w.o + w.n * FarInflate, w.u, w.n, len, 0f, w.top, null, sa, w.breaks);
                foreach (var o in plan.OpeningsOn(r, s))
                {
                    if (o.b >= 0 || o.oneSided) continue;
                    float c = M(o.center) - sa, hw = o.width * 0.05f;
                    if (c - hw < -0.001f || c + hw > len + 0.001f) continue;
                    float hTop = Mathf.Min(o.top, topY);
                    Flat(skin, rng.Chance(0.16) ? st.glassLit : st.glass, w, c - hw, c + hw, o.sill, hTop, 0.02f + FarInflate);
                }
            }
            if (!detail && !roofed) return;

            // lo zoccolo (non davanti alle porte) e la cornice
            if (detail)
            {
                float at = -0.14f;
                foreach (var d in doors)
                {
                    float a0 = d.x - 0.12f;
                    if (a0 > at + 0.05f) Blk(dress, st.plinth, w, at, a0, 0f, st.plinthH, 0f, 0.14f, false);
                    at = Mathf.Max(at, d.y + 0.12f);
                }
                if (len + 0.14f > at + 0.05f) Blk(dress, st.plinth, w, at, len + 0.14f, 0f, st.plinthH, 0f, 0.14f, false);
                foreach (var d in doors)
                {
                    // la soglia: un gradino basso davanti alla porta, e la porta incorniciata
                    Blk(dress, st.plinth, w, d.x - 0.25f, d.y + 0.25f, 0f, 0.12f, 0f, 0.85f, false);
                    float h = Mathf.Min(2.5f, (crumble && !LegacyWalls ? MinTop(w, d.x - 0.22f, d.y + 0.22f) : topY) - 0.2f);
                    bool gap = false;
                    foreach (var g in gaps)
                        if (Mathf.Abs(g.x - d.x) < 0.001f) { gap = true; h = Mathf.Max(0.3f, Mathf.Min(g.z, Mathf.Min(w.top(d.x), w.top(d.y))) - 0.06f); }
                    Blk(dress, st.trim, w, d.x - 0.18f, d.x - 0.04f, 0f, h + 0.05f, 0f, 0.08f, false);
                    Blk(dress, st.trim, w, d.y + 0.04f, d.y + 0.18f, 0f, h + 0.05f, 0f, 0.08f, false);
                    if (!gap) Blk(dress, st.trim, w, d.x - 0.22f, d.y + 0.22f, h + 0.02f, h + 0.24f, 0f, 0.14f);        // l'architrave: un varco non ce l'ha
                }
            }

            if (detail && FacadeDrift) Drift(dress, w, len, doors);

            if (flat && roofed) FlatEdge(dress, st, w, topY, crown, detail);
            else if (flat && detail) { Blk(dress, st.trim, w, -0.12f, len + 0.12f, topY - 0.04f, topY + 0.07f, InnerFlush, 0.09f); }      // il coronamento di un muro senza tetto
            else if (!flat && !crumble && detail && w.breaks != null && w.breaks.Length < 6)
            {
                // la testata di una capriata: un'asse lungo i due spioventi
                var xs = new List<float> { 0f }; xs.AddRange(w.breaks); xs.Add(len);
                for (int i = 0; i + 1 < xs.Count; i++)
                    dress.Beam(st.trim, w.P(xs[i], w.top(xs[i]) + 0.03f, 0.05f), w.P(xs[i + 1], w.top(xs[i + 1]) + 0.03f, 0.05f), 0.14f, 0.2f, w.n);
            }

            if (!detail) return;

            // lesene d'angolo (e a passo nei lunghi tratti) o contrafforti
            if (len > 1.6f && topY > 2.2f)
            {
                float ph0 = flat ? topY - 0.02f : Mathf.Min(topY, w.top(0f));
                float depth = ax ? 0.075f : 0.085f;           // due profondita': agli spigoli le due lesene non si toccano sullo stesso piano
                // su un muro crollato una lesena non e' piu' alta del muro nel suo punto (altrimenti spunta in aria sopra un dente piu' basso, o resta senza muro dietro)
                if (st.buttresses && r.church && topY > 3f)
                {
                    float pitch = 4.5f;
                    int n = Mathf.Max(1, Mathf.RoundToInt(len / pitch));
                    for (int i = 0; i <= n; i++)
                    {
                        float c = n == 0 ? len * 0.5f : len * i / n;
                        c = Mathf.Clamp(c, 0.35f, len - 0.35f);
                        if (Overlaps(cuts, c, 0.5f)) continue;
                        float ph = PillarTop(w, crumble, ph0, c - 0.6f, c + 0.6f);
                        if (ph < 1.3f) continue;
                        float h = Mathf.Max(1.2f, ph - 0.7f);
                        Blk(dress, st.wall, w, c - 0.3f, c + 0.3f, 0f, h, 0f, 0.55f, false);
                        Blk(dress, st.trim, w, c - 0.34f, c + 0.34f, h, h + 0.14f, 0f, 0.6f);
                        Blk(dress, st.wall, w, c - 0.26f, c + 0.26f, h + 0.14f, Mathf.Min(ph, h + 1.0f), 0f, 0.3f);
                    }
                }
                else if (st.pilasters)
                {
                    float p0 = PillarTop(w, crumble, ph0, -0.1f, 0.5f), p1 = PillarTop(w, crumble, ph0, len - 0.5f, len + 0.1f);
                    if (!Overlaps(cuts, 0.28f, 0.28f) && p0 > 0.5f) Blk(dress, st.trim, w, -0.1f, 0.5f, 0f, p0, 0f, depth, false);
                    if (!Overlaps(cuts, len - 0.28f, 0.28f) && p1 > 0.5f) Blk(dress, st.trim, w, len - 0.5f, len + 0.1f, 0f, p1, 0f, depth, false);
                    if (len > 7.5f)
                    {
                        int n = Mathf.FloorToInt(len / 4.5f);
                        for (int i = 1; i < n; i++)
                        {
                            float c = len * i / n;
                            if (Overlaps(cuts, c, 0.5f)) continue;
                            float pc = PillarTop(w, crumble, ph0, c - 0.22f, c + 0.22f);
                            if (pc > 0.5f) Blk(dress, st.trim, w, c - 0.22f, c + 0.22f, 0f, pc, 0f, depth * 0.8f, false);
                        }
                    }
                }
            }

            // i riquadri delle finestre vere: davanzale, stipiti, architrave, persiane
            foreach (var o in wins)
            {
                float c = M(o.center) - sa, hw = o.width * 0.05f;
                float hTop = Mathf.Min(o.top, (crumble && !LegacyWalls ? MinTop(w, c - hw - 0.14f, c + hw + 0.14f) : topY) - 0.05f);
                if (hTop - o.sill < 0.3f) continue;
                Blk(dress, st.trim, w, c - hw - 0.12f, c + hw + 0.12f, o.sill - 0.1f, o.sill + 0.02f, 0f, 0.18f);
                Blk(dress, st.trim, w, c - hw - 0.12f, c - hw + 0.01f, o.sill, hTop, 0f, 0.07f, false);
                Blk(dress, st.trim, w, c + hw - 0.01f, c + hw + 0.12f, o.sill, hTop, 0f, 0.07f, false);
                if (!o.Pointed) Blk(dress, st.trim, w, c - hw - 0.14f, c + hw + 0.14f, hTop, hTop + 0.17f, 0f, 0.12f);
                else Blk(dress, st.trim, w, c - 0.07f, c + 0.07f, hTop + 0.0f, hTop + 0.2f, 0f, 0.1f);
                if (st.shutters && !o.Pointed && hw < 0.9f && rng.Chance(0.55))
                {
                    float sw = hw * 0.95f, ang = rng.Chance(0.5) ? 0.02f : 0.28f;     // chiuse o appena aperte
                    Blk(dress, st.shutter, w, c - hw - 0.12f - sw, c - hw - 0.12f, o.sill - 0.02f, hTop, ang, ang + 0.04f, false);
                    Blk(dress, st.shutter, w, c + hw + 0.12f, c + hw + 0.12f + sw, o.sill - 0.02f, hTop, ang, ang + 0.04f, false);
                }
            }

            // finestre finte a passo regolare sui tratti nudi: il ritmo di una facciata (alcune accese di notte)
            if (st.blind && len >= 2.6f && topY >= 2.9f && !crumble)
            {
                bool church = r.church;
                float step = church ? 4.4f : 3.5f, margin = 1.35f;
                float sill = church ? 1.9f : 0.95f;
                float wTop = church ? Mathf.Min(sill + 2.9f, topY - 1.0f) : Mathf.Min(sill + 1.55f, topY - 0.65f);
                float hw = church ? 0.42f : 0.46f;
                if (wTop - sill > 0.7f)
                {
                    int count = Mathf.FloorToInt((len - 2f * margin) / step) + 1;
                    for (int i = 0; i < count; i++)
                    {
                        float c = count == 1 ? len * 0.5f : margin + (len - 2f * margin) * i / (count - 1);
                        if (Overlaps(cuts, c, hw + 0.2f)) continue;
                        bool lit = rng.Chance(church ? 0.08 : 0.22);
                        Flat(dress, lit ? st.glassLit : st.glass, w, c - hw, c + hw, sill, wTop, 0.02f);
                        Flat(dress, st.trim, w, c - hw - 0.1f, c - hw, sill - 0.02f, wTop + 0.1f, 0.03f);
                        Flat(dress, st.trim, w, c + hw, c + hw + 0.1f, sill - 0.02f, wTop + 0.1f, 0.03f);
                        Flat(dress, st.trim, w, c - hw - 0.1f, c + hw + 0.1f, wTop, wTop + 0.12f, 0.03f);
                        Blk(dress, st.trim, w, c - hw - 0.14f, c + hw + 0.14f, sill - 0.1f, sill, 0f, 0.14f, false);
                    }
                }
            }

            // i sassi dei crolli ai piedi del muro
            if (crumble)
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(len / 1.1f), 1, 14);
                for (int i = 0; i < n; i++)
                {
                    float c = rng.Range(0.2f, Mathf.Max(0.3f, len - 0.2f));
                    float sz = rng.Range(0.25f, 0.85f), d = rng.Range(0.2f, 1.7f);
                    Blk(dress, st.rubble, w, c - sz * 0.5f, c + sz * 0.5f, -0.05f, sz * rng.Range(0.5f, 1.0f), d, d + sz * rng.Range(0.7f, 1.3f), false);
                }
            }
        }

        /// <summary>L'altezza piu' bassa del profilo alto di un tratto tra x0 e x1 (il profilo e' fatto di tratti dritti tra i punti di svolta). Su un muro crollato le rifiniture non possono essere piu' alte del muro li'.</summary>
        static float MinTop(in Run w, float x0, float x1)
        {
            x0 = Mathf.Clamp(x0, 0f, w.len); x1 = Mathf.Clamp(x1, 0f, w.len);
            float m = Mathf.Min(w.top(x0), w.top(x1));
            if (w.breaks != null) foreach (var b in w.breaks) if (b > x0 && b < x1) m = Mathf.Min(m, w.top(b));
            return m;
        }

        /// <summary>L'altezza di una lesena o di un contrafforte tra x0 e x1: quella voluta, e su un muro crollato non piu' del muro li'.</summary>
        static float PillarTop(in Run w, bool crumble, float wanted, float x0, float x1) => crumble && !LegacyWalls ? Mathf.Min(wanted, MinTop(w, x0, x1) - 0.02f) : wanted;

        static bool Overlaps(List<Vector2> cuts, float c, float half)
        {
            foreach (var k in cuts) if (c + half > k.x && c - half < k.y) return true;
            return false;
        }

        /// <summary>Il bordo di un tetto piano: la cornice sotto e il parapetto sopra (con i merli, se 'crown').</summary>
        static void FlatEdge(MeshBuilder mb, FacadeStyle st, in Run w, float topY, bool crown, bool detail)
        {
            float len = w.len;
            float par = crown ? 0.95f : 0.55f;
            Blk(mb, st.parapet, w, -0.17f, len + 0.17f, topY - 0.06f, topY + 0.14f + par, InnerFlush, 0.10f);
            if (!detail) return;
            Blk(mb, st.trim, w, -0.2f, len + 0.2f, topY - 0.30f, topY - 0.04f, 0f, 0.22f, false);                      // la cornice sotto
            Blk(mb, st.trim, w, -0.2f, len + 0.2f, topY + 0.14f + par, topY + 0.14f + par + 0.09f, -0.34f, 0.14f);        // il coronamento
            if (crown)
            {
                int n = Mathf.Max(1, Mathf.FloorToInt(len / 1.15f));
                float pitch = len / n;
                for (int i = 0; i < n; i++)
                {
                    float c = pitch * (i + 0.5f);
                    Blk(mb, st.parapet, w, c - pitch * 0.28f, c + pitch * 0.28f, topY + 0.23f + par, topY + 0.23f + par + 0.5f, InnerFlush, 0.10f);
                }
            }
        }

        /// <summary>Il muro di una stanza alta sopra il tetto di una piu' bassa che gli sta accanto: dalla parte del vicino non ha facce (guarda solo dentro la sua stanza).</summary>
        static void StepRun(Plan plan, Room r, Side s, Seg seg, in Run w, FacadeStyle st, Room nb, MeshBuilder dress, bool detail)
        {
            float nbTop = nb.Open ? 0f : nb.crumble ? nb.wallH - Mathf.Min(1.7f, nb.wallH * 0.5f) : nb.wallH;
            if (!(nbTop < r.wallH - 0.25f)) return;
            float yMin = Mathf.Max(0.2f, nbTop) + (HasRoof(nb) ? 0.12f : 0f);
            var holes = new List<Hole>();
            foreach (var o in plan.OpeningsOn(r, s))
            {
                if (!o.oneSided) continue;
                float c = M(o.center) - w.sa, hw = o.width * 0.05f;
                if (c - hw < 0f || c + hw > w.len) continue;
                holes.Add(new Hole { c = c, hw = hw, o = o });
            }
            // la faccia dal nostro lato comincia dove finisce quella del vicino, a ogni ascissa: al suo profilo alto (il dente di un muro crollato, il timpano o l'arco di una navata) e, se ha il tetto, sopra
            // il tetto. Cominciare piu' in basso la sovrapporrebbe, a filo (stesso piano, stesso verso), alla faccia che il vicino ha dentro la sua stanza: le due si contendono i pixel e lampeggiano;
            // cominciare al punto piu' alto del profilo lascerebbe un buco tra i denti, da cui si vede dentro il muro
            Func<float, float> low = null; float[] lowBreaks = null;
            if (!LegacyWalls && exteriorBuild && !nb.Open)
            {
                bool ax = Plan.AlongX(s);
                float nbStart = (ax ? M(nb.x0) : M(nb.z0)) + TH, nbLen = (ax ? M(nb.x1) - M(nb.x0) : M(nb.z1) - M(nb.z0)) - 2f * TH;
                TopFunc(nb, ax, nbLen, out var nbTopAt, out var nbBreaks, (int)Plan.Opposite(s));
                float shiftNb = w.sa - nbStart, roofOff = HasRoof(nb) ? 0.12f : 0f;
                low = x => Mathf.Max(0.2f, nbTopAt(Mathf.Clamp(x + shiftNb, 0f, nbLen))) + roofOff;
                lowBreaks = ShiftBreaks(nbBreaks, shiftNb, w.len);
                yMin = 0.2f;
            }
            dress.WallFace(st.wall, w.o, w.u, w.n, w.len, yMin, w.top, holes, w.sa, w.breaks, low, lowBreaks);
            foreach (var h in holes) Flat(dress, st.glass, w, h.c - h.hw, h.c + h.hw, h.o.sill, Mathf.Min(h.o.top, w.top(h.c)), -0.06f);
            if (detail && w.breaks == null && Mathf.Abs(w.top(0f) - w.top(w.len)) < 0.01f && HasRoof(r))
                Blk(dress, st.trim, w, -0.12f, w.len + 0.12f, w.top(0f) - 0.26f, w.top(0f) + 0.05f, 0f, 0.22f, false);
        }

        // ------------------------------------------------------------------ i tetti
        static void AddRoof(Plan plan, Room r, float y0, FacadeStyle st, bool crown, bool detail, MeshBuilder mb, bool turrets = false)
        {
            float x0 = M(r.x0), x1 = M(r.x1), z0 = M(r.z0), z1 = M(r.z1);
            if (r.kind == RoomKind.Campanile) { Spire(mb, st, r, y0, detail); return; }
            switch (r.ceil)
            {
                case CeilKind.Gable:
                    if (r.ridgeH - r.wallH > 0.4f) { GableRoof(plan, mb, st, r, y0, detail); return; }
                    break;
                case CeilKind.BarrelRound:
                case CeilKind.BarrelPointed:
                    if (r.ridgeH - r.wallH > 0.3f) { VaultRoof(mb, st, r, y0); return; }
                    break;
            }
            // piano: una lastra sopra il soffitto (il parapetto lo mettono i tratti di muro sul vuoto)
            float y = y0 + r.wallH + 0.14f;
            Vector3 a = new Vector3(x0, y, z0), b = new Vector3(x0, y, z1), c = new Vector3(x1, y, z1), d = new Vector3(x1, y, z0);
            mb.Quad(st.roofFlat, a, b, c, d, new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0), Vector3.up);
            if (turrets) Turrets(plan, r, y, st, mb, detail);
            if (detail && r.kind != RoomKind.Corridoio && r.W * r.D > 2400)
            {
                var rng = new Rng(unchecked(r.seed * 31 + r.variant * 17 + 5));
                if (rng.Chance(0.4))
                {
                    // un comignolo
                    float cx = rng.Range(x0 + 1.2f, x1 - 1.2f), cz = rng.Range(z0 + 1.2f, z1 - 1.2f);
                    mb.Box(st.wall, new Vector3(cx, y + 0.8f, cz), new Vector3(0.8f, 1.6f, 0.8f));
                    mb.Box(st.trim, new Vector3(cx, y + 1.68f, cz), new Vector3(1.0f, 0.16f, 1.0f));
                }
                else if (rng.Chance(0.35))
                {
                    // un lucernario
                    float cx = rng.Range(x0 + 1.6f, x1 - 1.6f), cz = rng.Range(z0 + 1.6f, z1 - 1.6f);
                    mb.Box(st.trim, new Vector3(cx, y + 0.22f, cz), new Vector3(1.8f, 0.44f, 1.4f), 0f, false, false);
                    mb.Quad(st.glass, new Vector3(cx - 0.8f, y + 0.47f, cz - 0.6f), new Vector3(cx - 0.8f, y + 0.47f, cz + 0.6f), new Vector3(cx + 0.8f, y + 0.47f, cz + 0.6f), new Vector3(cx + 0.8f, y + 0.47f, cz - 0.6f),
                            Vector2.zero, Vector2.up, Vector2.one, Vector2.right, Vector3.up);
                }
            }
        }

        // ------------------------------------------------------------------ le torrette d'angolo di una torre
        /// <summary>Il punto (in decimetri) cade dentro una stanza del piano (quelle all'aperto non contano).</summary>
        static bool Covered(Plan plan, int x, int z)
        {
            foreach (var o in plan.rooms)
                if (!o.Open && x > o.x0 && x < o.x1 && z > o.z0 && z < o.z1) return true;
            return false;
        }

        /// <summary>Il lato 's' della stanza comincia (o finisce) in un angolo convesso e in fuori: anche l'altro lato che vi si incontra da' sul vuoto per il tratto accanto all'angolo, e in diagonale non c'e'
        /// nessuna stanza. E' l'unico angolo dove le due pelle esterne lasciano un vuoto (negli altri una si allunga dentro il muro dell'altra).</summary>
        static bool ConvexCorner(Plan plan, Room r, Side s, bool atStart)
        {
            Side perp; bool perpAtStart; int cx, cz, sx, sz;
            switch (s)
            {
                case Side.S: perp = atStart ? Side.W : Side.E; perpAtStart = true; cx = atStart ? r.x0 : r.x1; cz = r.z0; sx = atStart ? -1 : 1; sz = -1; break;
                case Side.N: perp = atStart ? Side.W : Side.E; perpAtStart = false; cx = atStart ? r.x0 : r.x1; cz = r.z1; sx = atStart ? -1 : 1; sz = 1; break;
                case Side.W: perp = atStart ? Side.S : Side.N; perpAtStart = true; cx = r.x0; cz = atStart ? r.z0 : r.z1; sx = -1; sz = atStart ? -1 : 1; break;
                default: perp = atStart ? Side.S : Side.N; perpAtStart = false; cx = r.x1; cz = atStart ? r.z0 : r.z1; sx = 1; sz = atStart ? -1 : 1; break;
            }
            var segs = plan.Segments(r, perp);
            if (segs.Count == 0) return false;
            var edge = perpAtStart ? segs[0] : segs[segs.Count - 1];
            return edge.other < 0 && !Covered(plan, cx + sx, cz + sz);
        }

        /// <summary>Le torrette agli angoli in fuori del tetto piano di una stanza dell'ultimo piano di una torre. Una torre era una scatola con i merli: dall'orizzonte non aveva nessuna sagoma. Un
        /// angolo e' in fuori se non c'e' nessuna stanza ne' di fronte all'angolo ne' accanto ai due muri che vi s'incontrano (si guardano tre punti a un decimetro da fuori); una stanza piu'
        /// stretta di 6 m non ne ha. Ogni torretta e' una scatola di pietra di 2,8-3,4 m che sporge di 25 cm dalla faccia dei muri (con il fondo: si vede da sotto), una cornice, un tetto a
        /// piramide di ardesia alto 2,6-3,6 m con una punta di ferro, e una feritoia scura su ognuna delle due facce in fuori (una su tre, accesa di notte). Misure e feritoie dipendono dal luogo e
        /// dall'angolo, quindi due torri non hanno le stesse torrette. Anche nella pelle di chi sta lontano: e' la parte grande della sagoma.</summary>
        internal static void Turrets(Plan plan, Room r, float roofY, FacadeStyle st, MeshBuilder mb, bool detail, float fixedSide = 0f, List<(Vector3 centre, Vector3 size)> solids = null)
        {
            if (r.W < 60 || r.D < 60) return;
            int[] xs = { r.x0, r.x1 }, zs = { r.z0, r.z1 };
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    int cx = xs[i], cz = zs[j], sx = i == 0 ? -1 : 1, sz = j == 0 ? -1 : 1;
                    if (Covered(plan, cx + sx, cz + sz) || Covered(plan, cx + sx, cz - sz) || Covered(plan, cx - sx, cz + sz)) continue;
                    var rng = new Rng(unchecked(r.seed * 37 + cx * 7 + cz * 13 + 3));
                    float side = rng.Range(2.8f, 3.4f), hb = rng.Range(3.2f, 4.4f), hr = rng.Range(2.6f, 3.6f);
                    if (fixedSide > 0f) side = fixedSide;              // il sagrato della cattedrale (DesertCathedral): torrette strette, per non toccare i banchi del mercato
                    float px = M(cx) - sx * (side * 0.5f - 0.40f), pz = M(cz) - sz * (side * 0.5f - 0.40f);          // la faccia in fuori sta 40 cm oltre la linea del muro (25 oltre la faccia)
                    mb.Box(st.wall, new Vector3(px, roofY + hb * 0.5f, pz), new Vector3(side, hb, side), 0f, false, true);
                    solids?.Add((new Vector3(px, roofY + hb * 0.5f, pz), new Vector3(side, hb, side)));          // chi cammina sul pavimento (il sagrato) non ci passa attraverso
                    float yc = roofY + hb + 0.15f;
                    mb.Box(st.trim, new Vector3(px, yc, pz), new Vector3(side + 0.5f, 0.3f, side + 0.5f), 0f, true, true);
                    // il tetto a piramide
                    float hw = (side + 0.5f) * 0.5f + 0.05f, yb = roofY + hb + 0.30f;
                    var apex = new Vector3(px, yb + hr, pz);
                    var a = new Vector3(px - hw, yb, pz - hw); var b = new Vector3(px + hw, yb, pz - hw); var c = new Vector3(px + hw, yb, pz + hw); var d = new Vector3(px - hw, yb, pz + hw);
                    float w2 = hw * 2f, sl = Mathf.Sqrt(hr * hr + hw * hw);
                    Vector2 u0 = new Vector2(0f, 0f), u1 = new Vector2(w2, 0f), u2 = new Vector2(w2 * 0.5f, sl);
                    mb.Quad(st.roofPitch, a, b, apex, apex, u0, u1, u2, u2, new Vector3(0, 1, -1));
                    mb.Quad(st.roofPitch, b, c, apex, apex, u0, u1, u2, u2, new Vector3(1, 1, 0));
                    mb.Quad(st.roofPitch, c, d, apex, apex, u0, u1, u2, u2, new Vector3(0, 1, 1));
                    mb.Quad(st.roofPitch, d, a, apex, apex, u0, u1, u2, u2, new Vector3(-1, 1, 0));
                    if (!LegacyWalls)
                    {
                        mb.Quad(st.roofPitch, a, b, apex, apex, u0, u1, u2, u2, new Vector3(0, -1, 1));
                        mb.Quad(st.roofPitch, b, c, apex, apex, u0, u1, u2, u2, new Vector3(-1, -1, 0));
                        mb.Quad(st.roofPitch, c, d, apex, apex, u0, u1, u2, u2, new Vector3(0, -1, -1));
                        mb.Quad(st.roofPitch, d, a, apex, apex, u0, u1, u2, u2, new Vector3(1, -1, 0));
                    }
                    if (detail) mb.Box(st.metal, apex + Vector3.up * 0.45f, new Vector3(0.1f, 0.9f, 0.1f));
                    // una feritoia su ognuna delle due facce in fuori
                    float y0 = roofY + 1.5f, y1 = roofY + Mathf.Min(hb - 0.6f, 3.0f);
                    var gx = rng.Chance(0.3) ? st.glassLit : st.glass; var gz = rng.Chance(0.3) ? st.glassLit : st.glass;
                    float fx = px + sx * (side * 0.5f + 0.02f), fz = pz + sz * (side * 0.5f + 0.02f);
                    mb.Quad(gx, new Vector3(fx, y0, pz - 0.2f), new Vector3(fx, y1, pz - 0.2f), new Vector3(fx, y1, pz + 0.2f), new Vector3(fx, y0, pz + 0.2f), Vector2.zero, Vector2.up, Vector2.one, Vector2.right, new Vector3(sx, 0f, 0f));
                    mb.Quad(gz, new Vector3(px - 0.2f, y0, fz), new Vector3(px - 0.2f, y1, fz), new Vector3(px + 0.2f, y1, fz), new Vector3(px + 0.2f, y0, fz), Vector2.zero, Vector2.up, Vector2.one, Vector2.right, new Vector3(0f, 0f, sz));
                }
        }

        static void GableRoof(Plan plan, MeshBuilder mb, FacadeStyle st, Room r, float y0, bool detail)
        {
            float x0 = M(r.x0), x1 = M(r.x1), z0 = M(r.z0), z1 = M(r.z1);
            bool ax = r.ridgeAlongX;
            float aLo = ax ? x0 : z0, aHi = ax ? x1 : z1, cLo = ax ? z0 : x0, cHi = ax ? z1 : x1;
            int eaveLo = EdgeStatus(plan, r, ax ? Side.S : Side.W), eaveHi = EdgeStatus(plan, r, ax ? Side.N : Side.E);
            int endLo = EdgeStatus(plan, r, ax ? Side.W : Side.S), endHi = EdgeStatus(plan, r, ax ? Side.E : Side.N);
            float eLo = eaveLo == 2 ? 0.5f : 0.12f, eHi = eaveHi == 2 ? 0.5f : 0.12f, gLo = endLo == 2 ? 0.28f : 0f, gHi = endHi == 2 ? 0.28f : 0f;
            float cm = (cLo + cHi) * 0.5f, half = (cHi - cLo) * 0.5f;
            float rise = r.ridgeH - r.wallH, slope = rise / half;
            float yEave = y0 + r.wallH + 0.12f, yRidge = y0 + r.ridgeH + 0.14f;
            Vector3 W(float a, float c, float y) => ax ? new Vector3(a, y, c) : new Vector3(c, y, a);
            float aA = aLo - gLo, aB = aHi + gHi, len = aB - aA;
            float cE0 = cLo - eLo, cE1 = cHi + eHi, yE0 = yEave - slope * eLo, yE1 = yEave - slope * eHi;
            Vector3 lowSide = ax ? new Vector3(0f, 1f, -1f) : new Vector3(-1f, 1f, 0f), highSide = ax ? new Vector3(0f, 1f, 1f) : new Vector3(1f, 1f, 0f);
            float sl0 = Mathf.Sqrt((cm - cE0) * (cm - cE0) + (yRidge - yE0) * (yRidge - yE0)), sl1 = Mathf.Sqrt((cE1 - cm) * (cE1 - cm) + (yRidge - yE1) * (yRidge - yE1));
            mb.Quad(st.roofPitch, W(aA, cE0, yE0), W(aB, cE0, yE0), W(aB, cm, yRidge), W(aA, cm, yRidge), new Vector2(0, 0), new Vector2(len, 0), new Vector2(len, sl0), new Vector2(0, sl0), lowSide);
            mb.Quad(st.roofPitch, W(aA, cE1, yE1), W(aB, cE1, yE1), W(aB, cm, yRidge), W(aA, cm, yRidge), new Vector2(0, 0), new Vector2(len, 0), new Vector2(len, sl1), new Vector2(0, sl1), highSide);
            if (!LegacyWalls)
            {
                // il sotto del tetto: una falda e' una sola faccia, da sotto non si disegna e tra il bordo del muro e la gronda (o lungo il timpano) si vede dentro l'edificio
                mb.Quad(st.roofPitch, W(aA, cE0, yE0), W(aB, cE0, yE0), W(aB, cm, yRidge), W(aA, cm, yRidge), new Vector2(0, 0), new Vector2(len, 0), new Vector2(len, sl0), new Vector2(0, sl0), -lowSide);
                mb.Quad(st.roofPitch, W(aA, cE1, yE1), W(aB, cE1, yE1), W(aB, cm, yRidge), W(aA, cm, yRidge), new Vector2(0, 0), new Vector2(len, 0), new Vector2(len, sl1), new Vector2(0, sl1), -highSide);
            }
            if (!detail) return;
            mb.Beam(st.trim, W(aA - 0.02f, cm, yRidge + 0.05f), W(aB + 0.02f, cm, yRidge + 0.05f), 0.32f, 0.2f, Vector3.up);
            mb.Beam(st.trim, W(aA, cE0, yE0 + 0.02f), W(aB, cE0, yE0 + 0.02f), 0.1f, 0.22f, Vector3.up);
            mb.Beam(st.trim, W(aA, cE1, yE1 + 0.02f), W(aB, cE1, yE1 + 0.02f), 0.1f, 0.22f, Vector3.up);
            if (endLo == 2) { mb.Beam(st.trim, W(aA, cE0, yE0 + 0.02f), W(aA, cm, yRidge), 0.1f, 0.2f, Vector3.up); mb.Beam(st.trim, W(aA, cE1, yE1 + 0.02f), W(aA, cm, yRidge), 0.1f, 0.2f, Vector3.up); }
            if (endHi == 2) { mb.Beam(st.trim, W(aB, cE0, yE0 + 0.02f), W(aB, cm, yRidge), 0.1f, 0.2f, Vector3.up); mb.Beam(st.trim, W(aB, cE1, yE1 + 0.02f), W(aB, cm, yRidge), 0.1f, 0.2f, Vector3.up); }
        }

        static void VaultRoof(MeshBuilder mb, FacadeStyle st, Room r, float y0)
        {
            float x0 = M(r.x0), x1 = M(r.x1), z0 = M(r.z0), z1 = M(r.z1);
            bool ax = r.ridgeAlongX, pointed = r.ceil == CeilKind.BarrelPointed;
            float aLo = ax ? x0 : z0, aHi = ax ? x1 : z1, cLo = ax ? z0 : x0, cHi = ax ? z1 : x1;
            float cm = (cLo + cHi) * 0.5f, half = (cHi - cLo) * 0.5f, rise = r.ridgeH - r.wallH;
            Vector3 W(float a, float c, float y) => ax ? new Vector3(a, y, c) : new Vector3(c, y, a);
            float yb = y0 + r.wallH + 0.14f;
            int n = 14;
            for (int i = 0; i < n; i++)
            {
                float d0 = -half + 2f * half * i / n, d1 = -half + 2f * half * (i + 1) / n;
                float h0 = Arch.Y(d0, half, rise, pointed), h1 = Arch.Y(d1, half, rise, pointed);
                Vector3 n0 = Slope(d0, half, rise, pointed, ax), n1 = Slope(d1, half, rise, pointed, ax);
                float len = aHi - aLo;
                mb.QuadSmooth(st.roofFlat, W(aLo, cm + d0, yb + h0), W(aHi, cm + d0, yb + h0), W(aHi, cm + d1, yb + h1), W(aLo, cm + d1, yb + h1), n0, n0, n1, n1,
                              new Vector2(0, d0 + half), new Vector2(len, d0 + half), new Vector2(len, d1 + half), new Vector2(0, d1 + half));
                // il sotto della volta (da sotto si vedrebbe attraverso, dentro la chiesa): la stessa faccia rivolta in giu'
                if (!LegacyWalls)
                    mb.QuadSmooth(st.roofFlat, W(aLo, cm + d0, yb + h0), W(aHi, cm + d0, yb + h0), W(aHi, cm + d1, yb + h1), W(aLo, cm + d1, yb + h1), -n0, -n0, -n1, -n1,
                                  new Vector2(0, d0 + half), new Vector2(len, d0 + half), new Vector2(len, d1 + half), new Vector2(0, d1 + half));
            }
        }

        static Vector3 Slope(float d, float half, float rise, bool pointed, bool ax)
        {
            float e = 0.02f;
            float dy = Arch.Y(Mathf.Clamp(d + e, -half, half), half, rise, pointed) - Arch.Y(Mathf.Clamp(d - e, -half, half), half, rise, pointed);
            var t = new Vector2(2f * e, dy).normalized;
            var nrm = new Vector2(-t.y, t.x);             // nel piano (attraverso la volta, altezza): in su e verso l'esterno
            return ax ? new Vector3(0f, nrm.y, nrm.x) : new Vector3(nrm.x, nrm.y, 0f);
        }

        /// <summary>La guglia di un campanile: una piramide d'ardesia con un fiore sulla punta.</summary>
        static void Spire(MeshBuilder mb, FacadeStyle st, Room r, float y0, bool detail)
        {
            float x0 = M(r.x0) - 0.35f, x1 = M(r.x1) + 0.35f, z0 = M(r.z0) - 0.35f, z1 = M(r.z1) + 0.35f;
            float yb = y0 + r.wallH + 0.12f, h = Mathf.Max(4.5f, 0.95f * Mathf.Min(x1 - x0, z1 - z0));
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            var apex = new Vector3(cx, yb + h, cz);
            Vector3 a = new Vector3(x0, yb, z0), b = new Vector3(x1, yb, z0), c = new Vector3(x1, yb, z1), d = new Vector3(x0, yb, z1);
            float wx = x1 - x0, wz = z1 - z0, sx = Mathf.Sqrt(h * h + wz * wz * 0.25f), sz = Mathf.Sqrt(h * h + wx * wx * 0.25f);
            mb.Quad(st.roofPitch, a, b, apex, apex, new Vector2(0, 0), new Vector2(wx, 0), new Vector2(wx * 0.5f, sx), new Vector2(wx * 0.5f, sx), new Vector3(0, 1, -1));
            mb.Quad(st.roofPitch, b, c, apex, apex, new Vector2(0, 0), new Vector2(wz, 0), new Vector2(wz * 0.5f, sz), new Vector2(wz * 0.5f, sz), new Vector3(1, 1, 0));
            mb.Quad(st.roofPitch, c, d, apex, apex, new Vector2(0, 0), new Vector2(wx, 0), new Vector2(wx * 0.5f, sx), new Vector2(wx * 0.5f, sx), new Vector3(0, 1, 1));
            mb.Quad(st.roofPitch, d, a, apex, apex, new Vector2(0, 0), new Vector2(wz, 0), new Vector2(wz * 0.5f, sz), new Vector2(wz * 0.5f, sz), new Vector3(-1, 1, 0));
            if (!LegacyWalls)
            {
                // il sotto della guglia: da sotto si vedrebbe dentro la piramide
                mb.Quad(st.roofPitch, a, b, apex, apex, new Vector2(0, 0), new Vector2(wx, 0), new Vector2(wx * 0.5f, sx), new Vector2(wx * 0.5f, sx), new Vector3(0, -1, 1));
                mb.Quad(st.roofPitch, b, c, apex, apex, new Vector2(0, 0), new Vector2(wz, 0), new Vector2(wz * 0.5f, sz), new Vector2(wz * 0.5f, sz), new Vector3(-1, -1, 0));
                mb.Quad(st.roofPitch, c, d, apex, apex, new Vector2(0, 0), new Vector2(wx, 0), new Vector2(wx * 0.5f, sx), new Vector2(wx * 0.5f, sx), new Vector3(0, -1, -1));
                mb.Quad(st.roofPitch, d, a, apex, apex, new Vector2(0, 0), new Vector2(wz, 0), new Vector2(wz * 0.5f, sz), new Vector2(wz * 0.5f, sz), new Vector3(1, -1, 0));
            }
            if (detail)
            {
                mb.Box(st.metal, apex + Vector3.up * 0.5f, new Vector3(0.1f, 1.0f, 0.1f));
                mb.Box(st.metal, apex + Vector3.up * 0.75f, new Vector3(0.5f, 0.09f, 0.09f));
            }
        }
    }
}
