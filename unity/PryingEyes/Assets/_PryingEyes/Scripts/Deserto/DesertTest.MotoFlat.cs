#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    /// <summary>-desertonly motopiano: la moto che "sbatte contro cose invisibili" su sabbia piana, a velocita' alta (segnalazione del 2026-11-20). Non si puo' ripetere a mano, quindi si cerca con quattro rivelatori:
    /// (1) i collider delle cose sulla sabbia contro la forma che si vede (una capsula piu' larga o piu' alta di quello che si disegna e' un muro invisibile); (2) le giunzioni tra i blocchi di sabbia (un gradino
    /// da un blocco all'altro e' un muro a 80 km/h); (3) due corse CIECHE, a 83 e a 112 km/h (gas e turbo, nessun pilota automatico che schivi), su una rotta di 2,5 km scelta dal generatore delle cose
    /// (nessuna cosa solida a meno di tre metri dalla traiettoria, nessun luogo a meno di 110 m, pendenza bassa): su una rotta cosi' ogni frenata e' un difetto, e il controller dice contro che cosa
    /// ha battuto (PlayerController.RideLastContact); (4) durante le corse, quanta sabbia manca davanti (i blocchi che non ci sono ancora o che hanno il dettaglio sbagliato), quanto la moto frena
    /// perche' il terreno davanti non e' pronto e quali fotogrammi lunghi coincidono con la nascita dei blocchi.</summary>
    public sealed partial class DesertTest
    {
        // ------------------------------------------------------------------ 1. i collider contro le forme
        /// <summary>Per ogni genere con un collider (DesertScatterGen.Solid): quanto la capsula sporge dalla forma che si vede (la scatola che racchiude la mesh), di lato (la distanza piu' grande tra un punto del
        /// cerchio della capsula e la scatola) e in alto. Oltre 0,6 m di lato (il muso della moto sta a 0,95 m dal corpo, che ha un raggio di 0,35) o 0,5 m in alto (l'altezza che il controller scavalca)
        /// la capsula e' un muro che non si vede. Torna quanti generi sporgono.</summary>
        int PropColliderAudit()
        {
            int ghosts = 0;
            const float bottom = 0.1f;                       // DesertScatter.Colliders_: il fondo della capsula sta 10 cm sopra il piede
            var sb = new StringBuilder();
            for (int k = 0; k < (int)Prop.Count; k++)
            {
                var kind = (Prop)k;
                DesertScatterGen.Solid(kind, out float r, out float h);
                if (r <= 0f) continue;
                var mesh = PropMeshes.Get(kind);
                if (mesh == null) continue;
                var b = mesh.bounds;
                float height = Mathf.Max(h, r * 2f), top = bottom + height;
                float side = 0f;
                for (int a = 0; a < 360; a += 5)
                {
                    float px = Mathf.Cos(a * Mathf.Deg2Rad) * r, pz = Mathf.Sin(a * Mathf.Deg2Rad) * r;
                    float dx = Mathf.Max(Mathf.Max(b.min.x - px, px - b.max.x), 0f), dz = Mathf.Max(Mathf.Max(b.min.z - pz, pz - b.max.z), 0f);
                    side = Mathf.Max(side, Mathf.Sqrt(dx * dx + dz * dz));
                }
                float up = Mathf.Max(0f, top - b.max.y);
                // l'opposto: quanto la forma esce dalla capsula (si attraversa il disegno): il lato piu' lontano della scatola dall'asse, meno il raggio
                float beyond = Mathf.Max(0f, Mathf.Max(Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x)), Mathf.Max(Mathf.Abs(b.min.z), Mathf.Abs(b.max.z))) - r);
                bool ghost = side > 0.6f;
                if (ghost) ghosts++;
                sb.Append($"\n    {kind,-12} capsula r {r:F2} x h {height:F2} (da {bottom:F1} a {top:F2}); forma {b.size.x:F2} x {b.size.y:F2} x {b.size.z:F2} (da {b.min.y:F2} a {b.max.y:F2}); sporge di lato {side:F2} m, in alto {up:F2} m; il disegno esce dalla capsula di {beyond:F2} m{(ghost ? "  <-- MURO CHE NON SI VEDE" : "")}");
            }
            Debug.Log("[PryingEyes] desertotest motopiano: collider delle cose contro le loro forme (scala 1):" + sb);
            return ghosts;
        }

        // ------------------------------------------------------------------ 2. le giunzioni della sabbia
        /// <summary>I confini tra blocchi vicini (entro 120 m, tutti con il collider): a tre centimetri dai due lati il suolo ha la stessa quota? Un gradino o un buco e' un muro (o una caduta) a 80 km/h.
        /// Non conta dove su uno dei due lati c'e' una cosa (un sasso) invece della sabbia.</summary>
        void SeamAudit(Vector3 at, out int samples, out int steps, out float worst, out int holes)
        {
            samples = steps = holes = 0; worst = 0f;
            float s = DesertTerrainGen.ChunkSize;
            int pcx = Mathf.FloorToInt(at.x / s), pcz = Mathf.FloorToInt(at.z / s);
            var sb = new StringBuilder();
            bool Sand(float x, float z, out float y)
            {
                y = 0f;
                if (!Physics.Raycast(new Vector3(x, DesertWorld.Height(x, z) + 40f, z), Vector3.down, out var h, 90f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) return false;
                y = h.point.y;
                return DesertTerrain.IsSand(h.collider);
            }
            for (int i = -2; i <= 3; i++)
                for (int j = -2; j <= 3; j++)
                    for (int axis = 0; axis < 2; axis++)
                    {
                        float line = axis == 0 ? (pcx + i) * s : (pcz + j) * s;
                        for (float t = 0.6f; t < s; t += 1.7f)
                        {
                            float x = axis == 0 ? line : (pcx + i) * s + t, z = axis == 0 ? (pcz + j) * s + t : line;
                            if ((new Vector3(x, 0f, z) - new Vector3(at.x, 0f, at.z)).magnitude > 120f) continue;
                            float ax = axis == 0 ? 0.03f : 0f, az = axis == 0 ? 0f : 0.03f;
                            bool a = Sand(x - ax, z - az, out float ya), b = Sand(x + ax, z + az, out float yb);
                            if (!a && !b) continue;
                            samples++;
                            if (a != b) { holes++; if (sb.Length < 600) sb.Append($" [buco in {x:F1},{z:F1}: {(a ? "manca il lato +" : "manca il lato -")}]"); continue; }
                            // il dislivello vero tra i due punti (la funzione dell'altezza) e' pendenza x sei centimetri: un fianco ripido non e' una giunzione. Conta quello che la sabbia fa in piu'.
                            float d = Mathf.Abs(ya - yb) - Mathf.Abs(DesertWorld.Height(x - ax, z - az) - DesertWorld.Height(x + ax, z + az));
                            if (d > worst) worst = d;
                            if (d > 0.04f) { steps++; if (sb.Length < 600) sb.Append($" [gradino di {d:F2} m in {x:F1},{z:F1}]"); }
                        }
                    }
            Debug.Log($"[PryingEyes] desertotest motopiano: giunzioni della sabbia: {samples} campioni, {steps} gradini oltre 4 cm (oltre il dislivello vero), {holes} buchi, scarto massimo {worst * 100f:F1} cm{sb}");
        }

        // ------------------------------------------------------------------ 3. la rotta sgombra
        readonly Dictionary<long, List<PropItem>> flatProps = new Dictionary<long, List<PropItem>>();
        readonly List<Site> flatSites = new List<Site>();
        const float FlatSteepDeg = 12f;                         // oltre questa pendenza (gradi) davanti, una frenata o un urto e' contro un fianco di duna, non sabbia piana
        readonly int[] flatFail = new int[3];                   // perche' i tratti candidati sono stati scartati: pendenza, luogo vicino, cosa solida

        List<PropItem> FlatPropsOf(int cx, int cz)
        {
            long k = ((long)cx << 32) ^ (uint)cz;
            if (!flatProps.TryGetValue(k, out var l)) { l = new List<PropItem>(); DesertScatterGen.Build(cx, cz, l); flatProps[k] = l; }
            return l;
        }

        static float SegDist2(float px, float pz, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az, len2 = dx * dx + dz * dz;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(((px - ax) * dx + (pz - az) * dz) / len2);
            float qx = ax + dx * t - px, qz = az + dz * t - pz;
            return qx * qx + qz * qz;
        }

        /// <summary>Il tratto a-b e' sgombro: nessuna cosa con un collider a meno di 'margin' m dal suo bordo, nessun luogo a meno di 'siteMargin' m, e la pendenza (su quattro metri) non supera 'slope'.</summary>
        bool FlatClean(Vector3 a, Vector3 b, float margin, float siteMargin, float slope, ref float maxSlope)
        {
            float len = Flat(a, b); int n = Mathf.Max(2, Mathf.CeilToInt(len / 4f));
            float hPrev = DesertWorld.Height(a.x, a.z), worstHere = 0f;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n, x = Mathf.Lerp(a.x, b.x, t), z = Mathf.Lerp(a.z, b.z, t), hh = DesertWorld.Height(x, z);
                worstHere = Mathf.Max(worstHere, Mathf.Abs(hh - hPrev) / (len / n));
                hPrev = hh;
            }
            if (worstHere > slope) { flatFail[0]++; return false; }
            for (float d = 0f; d <= len + 0.1f; d += 32f)
            {
                float t = len < 0.1f ? 0f : d / len;
                DesertWorld.SitesNear(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.z, b.z, t), siteMargin, flatSites);
                if (flatSites.Count > 0) { flatFail[1]++; return false; }
            }
            float s = DesertScatterGen.Chunk, pad = DesertScatterGen.Leak + 6f;
            int c0x = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - pad) / s), c1x = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + pad) / s);
            int c0z = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - pad) / s), c1z = Mathf.FloorToInt((Mathf.Max(a.z, b.z) + pad) / s);
            for (int cx = c0x; cx <= c1x; cx++)
                for (int cz = c0z; cz <= c1z; cz++)
                    foreach (var it in FlatPropsOf(cx, cz))
                    {
                        DesertScatterGen.Solid((Prop)it.kind, out float r, out float hgt);
                        if (r <= 0f) continue;
                        float reach = r * it.scale + margin;
                        if (SegDist2(it.x, it.z, a.x, a.z, b.x, b.z) < reach * reach) { flatFail[2]++; return false; }
                    }
            maxSlope = Mathf.Max(maxSlope, worstHere);
            return true;
        }

        /// <summary>Una rotta a tratti da 64 m da 'from' verso 'heading' (radianti): ogni tratto parte dritto e, se non e' sgombro o e' troppo ripido, gira a destra o a sinistra a passi di 8 gradi (fino a 32), senza mai
        /// allontanarsi di piu' di 50 gradi dalla direzione di partenza. Si ferma al primo tratto che non trova strada.</summary>
        List<Vector3> PlanFlatRoute(Vector3 from, float heading, int segments, float slope, out float maxSlope)
        {
            maxSlope = 0f;
            const float seg = 64f;
            var pts = new List<Vector3> { from };
            Vector3 cur = from; float h = heading;
            var deltas = new float[] { 0f, 8f, -8f, 16f, -16f, 24f, -24f, 32f, -32f };
            for (int i = 0; i < segments; i++)
            {
                bool ok = false; Vector3 found = Vector3.zero; float newH = h;
                foreach (float d in deltas)
                {
                    float hh = h + d * Mathf.Deg2Rad;
                    if (Mathf.Abs(Mathf.DeltaAngle(hh * Mathf.Rad2Deg, heading * Mathf.Rad2Deg)) > 50f) continue;
                    Vector3 cand = cur + new Vector3(Mathf.Sin(hh), 0f, Mathf.Cos(hh)) * seg; cand.y = DesertWorld.Height(cand.x, cand.z);
                    if (!FlatClean(cur, cand, 3.0f, 45f, slope, ref maxSlope)) continue;
                    found = cand; newH = hh; ok = true; break;
                }
                if (!ok) break;
                pts.Add(found); cur = found; h = newH;
            }
            return pts;
        }

        /// <summary>La rotta piu' lunga (fino a 'segments' tratti) tra sedici direzioni, con la pendenza massima piu' bassa che ne da' una completa (10, 17 e 24 gradi su quattro metri).</summary>
        List<Vector3> BestFlatRoute(Vector3 from, int segments, out float heading, out float maxSlope)
        {
            List<Vector3> best = null; heading = 0f; maxSlope = 0f;
            foreach (float slope in new[] { 0.18f, 0.3f, 0.45f })
            {
                for (int k = 0; k < 16; k++)
                {
                    float h = k * (Mathf.PI * 2f / 16f);
                    var r = PlanFlatRoute(from, h, segments, slope, out float ms);
                    if (best == null || r.Count > best.Count) { best = r; heading = h; maxSlope = ms; }
                    if (r.Count > segments) return r;
                }
                if (best != null && best.Count > segments) break;
            }
            return best;
        }

        // ------------------------------------------------------------------ 4. la corsa cieca
        sealed class FlatStats
        {
            public string tag; public bool boost, reached;
            public float secs, dist, vSum, vMax, hpLost, worstDt, brakeSecs, nearestMissing = 9999f, nearestCoarse = 9999f, nearestCell = 9999f, applyDtSum, applyDtMax, plainDtSum, plainDtMax;
            public int frames, steadyFrames, dips, crashes, dipsSteep, crashesSteep, brakeFrames, hitches, probes, maxPendingCells, maxPendingJobs, missingMax, coarseMax, applyFrames, plainFrames, explained;
            public readonly List<float> popDists = new List<float>();             // a che distanza (m dal bordo) un luogo che era oltre 770 m e' diventato visibile (pelle lontana finita o guscio completo)
        }

        static float SiteEdge(Site s, Vector3 p)
        {
            float dx = Mathf.Max(Mathf.Max(s.fx0 - p.x, p.x - s.fx1), 0f), dz = Mathf.Max(Mathf.Max(s.fz0 - p.z, p.z - s.fz1), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Il luogo si vede: il guscio vero e' completo, oppure la pelle lontana e' accesa e finita.</summary>
        static bool SiteVisible(Site s)
        {
            var rt = RtOf(s);
            if (rt != null && rt.ShellComplete) return true;
            var skin = Dd != null ? Dd.FarSkinOf(s) : null;
            return skin != null && skin.Shown && skin.Done;
        }

        readonly Dictionary<long, int> flatHave = new Dictionary<long, int>();

        /// <summary>Quanta sabbia manca attorno a 'p' (nella finestra della vista, 740 m): i blocchi che non ci sono e quelli che hanno meno dettaglio di quello che la distanza vuole (meno di 150 m: senza collider;
        /// meno di 380: passo di 4 m), con la distanza del piu' vicino di ciascun tipo. Il dettaglio si riconosce dal numero di vertici (1.221, 357 e 117 senza buchi).</summary>
        void SandCoverage(Vector3 p, out float nearestMissing, out float nearestCoarse, out int missing, out int coarse)
        {
            nearestMissing = nearestCoarse = 9999f; missing = coarse = 0;
            var terr = DesertTerrain.I;
            if (terr == null) return;
            flatHave.Clear();
            var tr = terr.transform;
            for (int i = 0; i < tr.childCount; i++)
            {
                var ch = tr.GetChild(i);
                var parts = ch.name.Split('_');
                if (parts.Length != 3 || parts[0] != "Sabbia" || !int.TryParse(parts[1], out int cx) || !int.TryParse(parts[2], out int cz)) continue;
                var mf = ch.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                int vc = mf.sharedMesh.vertexCount;
                flatHave[((long)cx << 32) ^ (uint)cz] = vc >= 800 ? 0 : vc >= 250 ? 1 : 2;
            }
            float s = DesertTerrainGen.ChunkSize;
            int pcx = Mathf.FloorToInt(p.x / s), pcz = Mathf.FloorToInt(p.z / s), r = Mathf.CeilToInt(DesertTerrain.Lod2 / s) + 1;
            for (int i = -r; i <= r; i++)
                for (int j = -r; j <= r; j++)
                {
                    int cx = pcx + i, cz = pcz + j;
                    float x0 = cx * s, z0 = cz * s;
                    float dx = Mathf.Max(Mathf.Max(x0 - p.x, p.x - (x0 + s)), 0f), dz = Mathf.Max(Mathf.Max(z0 - p.z, p.z - (z0 + s)), 0f), d = Mathf.Sqrt(dx * dx + dz * dz);
                    int want = d < DesertTerrain.Lod0 ? 0 : d < DesertTerrain.Lod1 ? 1 : d < DesertTerrain.Lod2 ? 2 : -1;
                    if (want < 0) continue;
                    if (!flatHave.TryGetValue(((long)cx << 32) ^ (uint)cz, out int have)) { missing++; nearestMissing = Mathf.Min(nearestMissing, d); }
                    else if (have > want) { coarse++; nearestCoarse = Mathf.Min(nearestCoarse, d); }
                }
        }

        /// <summary>Gli ultimi 24 fotogrammi che RideTick ha registrato (PlayerController.RideTrace): passo in ms, velocita' comandata > fatta (m/s), perdita, accumulo della frenata da urto.</summary>
        static string FlatTrace(PlayerController pl)
        {
            var sb = new StringBuilder();
            int n = pl.RideTrace.Length, next = pl.RideTraceNext;
            for (int k = 0; k < 24; k++)
            {
                var f = pl.RideTrace[(((next - 24 + k) % n) + n) % n];
                sb.Append($" {f.dt * 1000f:F1}ms {f.along:F2}>{f.done:F2} L{f.loss:F2} A{f.acc:F1}{(f.grounded ? "" : " ARIA")} |");
            }
            return sb.ToString();
        }

        string FlatContact(PlayerController pl, CharacterController cc)
        {
            var c = pl.RideLastContact;
            var sb = new StringBuilder();
            if (c.collider != null && Time.frameCount - c.frame <= 4)
                sb.Append($"contatto laterale di {Time.frameCount - c.frame} fotogrammi fa: {Describe(c.collider, cc)}, sabbia {DesertTerrain.IsSand(c.collider)}, a {Vector3.Distance(c.point, pl.transform.position):F1} m, normale {c.normal.x:F2},{c.normal.y:F2},{c.normal.z:F2}");
            else sb.Append("nessun contatto laterale");
            // i luoghi entro 150 m: che cosa sono, a che distanza e' il bordo della loro pianta, se il guscio c'e' ed e' completo, se la pelle lontana e' accesa
            var sites = new List<Site>();
            DesertWorld.SitesNear(pl.transform.position.x, pl.transform.position.z, 150f, sites);
            foreach (var s in sites)
            {
                float dx = Mathf.Max(Mathf.Max(s.fx0 - pl.transform.position.x, pl.transform.position.x - s.fx1), 0f), dz = Mathf.Max(Mathf.Max(s.fz0 - pl.transform.position.z, pl.transform.position.z - s.fz1), 0f);
                var rt = RtOf(s); var skin = Dd != null ? Dd.FarSkinOf(s) : null;
                sb.Append($"; luogo {s.kind}/{s.state} cella {s.cx},{s.cz} pianta x {s.fx0:F0}..{s.fx1:F0} z {s.fz0:F0}..{s.fz1:F0}, bordo a {Mathf.Sqrt(dx * dx + dz * dz):F0} m, guscio {(rt == null ? "non caricato" : rt.ShellComplete ? "completo" : "in costruzione")}, pelle lontana {(skin == null ? "nessuna" : skin.Shown ? "accesa" : "spenta")}");
            }
            if (c.collider != null && Time.frameCount - c.frame <= 4)
            {
                var chain = new StringBuilder(); var tr = c.collider.transform;
                for (int up = 0; up < 4 && tr != null; up++, tr = tr.parent) chain.Append(tr.name).Append(" < ");
                int rend = 0, rendOn = 0;
                foreach (var r in c.collider.transform.parent != null ? c.collider.transform.parent.GetComponentsInChildren<Renderer>() : new Renderer[0]) { rend++; if (r.enabled && r.gameObject.activeInHierarchy) rendOn++; }
                sb.Append($"; il collider sta in {chain}, e nei dintorni del suo genitore ci sono {rend} disegni ({rendOn} accesi)");
            }
            var near = Physics.OverlapSphere(pl.transform.position + Vector3.up * 0.9f, 2.2f, ~0, QueryTriggerInteraction.Ignore);
            int shown = 0;
            foreach (var o in near)
            {
                if (o == null || o == cc || DesertTerrain.IsSand(o)) continue;
                if (shown++ < 4) sb.Append($"; vicino {Describe(o, cc)} a {Vector3.Distance(o.ClosestPoint(pl.transform.position + Vector3.up * 0.9f), pl.transform.position + Vector3.up * 0.9f):F1} m");
            }
            return sb.ToString();
        }

        /// <summary>Guida CIECA lungo la rotta: gas (e turbo), il manubrio punta al prossimo punto, nessuno schiva niente. Misura le frenate, gli urti, il terreno non pronto, la sabbia che manca e i fotogrammi lunghi.</summary>
        IEnumerator RideFlat(List<Vector3> route, bool boost, FlatStats st)
        {
            var pl = Pl;
            var cc = pl.GetComponent<CharacterController>();
            var terr = DesertTerrain.I; var scatter = DesertScatter.I;
            DesertBike.botThrottle = 1f; DesertBike.botBoost = boost; DesertBike.botSteer = 0f;
            int next = 1;
            float t0 = Time.time, lastClear = t0, lastProbe = t0, lastDip = -9f, prevCool = 0f;
            float timeout = Flat(route[0], route[route.Count - 1]) / (boost ? 18f : 13f) + 30f;
            int appliedPrev = terr != null ? terr.Applied : 0;
            float lifePrev = pl.hp + pl.shield;
            var recent = new Queue<(float t, float v)>();
            Vector3 origin = pl.transform.position, last = origin;
            // i luoghi che all'inizio sono oltre 770 m: se ne nota la distanza alla quale diventano visibili
            var popWatch = new Dictionary<Site, bool>();
            var popList = new List<Site>();
            DesertWorld.SitesNearKnown(origin.x, origin.z, 1500f, popList);
            foreach (var s in popList) if (SiteEdge(s, origin) > 770f) popWatch[s] = false;
            while (pl.Riding && Time.time - t0 < timeout)
            {
                Vector3 pos = pl.transform.position;
                while (next < route.Count - 1 && Flat(pos, route[next]) < 24f) next++;
                if (next == route.Count - 1 && Flat(pos, route[next]) < 24f) { st.reached = true; break; }
                Vector3 aim = route[next] - pos; aim.y = 0f;
                float diff = Mathf.DeltaAngle(pl.RideHeading * Mathf.Rad2Deg, Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg);
                DesertBike.botSteer = Mathf.Clamp(diff / 20f, -1f, 1f);

                float now = Time.time, dt = Time.unscaledDeltaTime, v = pl.RideSpeed;
                Vector3 fwd = new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading));
                st.frames++; st.vSum += v; st.vMax = Mathf.Max(st.vMax, v); st.worstDt = Mathf.Max(st.worstDt, dt);
                if (dt > 0.1f) st.hitches++;
                motoWorst = Mathf.Max(motoWorst, dt);
                flatLedger?.Sample(pos, dt);
                // i primi 60 m non contano per la sabbia (la partenza da fermo): si parte da un mondo finito (FlatSettle), e quello che manca dopo e' colpa della velocita'
                bool steady = Flat(pos, origin) > 60f;
                if (terr != null)
                {
                    int applied = terr.Applied; bool births = applied > appliedPrev; appliedPrev = applied;
                    if (steady)
                    {
                        st.steadyFrames++;
                        if (births) { st.applyFrames++; st.applyDtSum += dt; st.applyDtMax = Mathf.Max(st.applyDtMax, dt); }
                        else { st.plainFrames++; st.plainDtSum += dt; st.plainDtMax = Mathf.Max(st.plainDtMax, dt); }
                        st.maxPendingJobs = Mathf.Max(st.maxPendingJobs, terr.PendingJobs);
                    }
                    // la stessa prova di PlayerController.RideTick: il terreno davanti non e' pronto e la moto frena da sola (sempre: anche all'inizio e' un freno vero)
                    if (Mathf.Abs(v) > 7f && !terr.Ready(pos + fwd * (Mathf.Abs(v) * 0.9f + 8f), 0f)) { st.brakeFrames++; st.brakeSecs += Mathf.Min(dt, 0.05f); }
                }
                if (scatter != null && steady) st.maxPendingCells = Mathf.Max(st.maxPendingCells, scatter.CellsPending);

                // una frenata: la velocita' scende di 3 m/s o piu' rispetto al massimo dell'ultimo terzo di secondo (e' il gas a fondo: non c'e' altro che la tolga)
                recent.Enqueue((now, v));
                while (recent.Count > 0 && now - recent.Peek().t > 0.35f) recent.Dequeue();
                float vRecent = 0f; foreach (var e in recent) vRecent = Mathf.Max(vRecent, e.v);
                float cool = pl.RideCrashCooldown;
                bool crash = cool > 0.7f && prevCool <= 0.7f; prevCool = cool;
                if ((vRecent - v > 3f && vRecent > 8f && now - lastDip > 1.2f) || crash)
                {
                    lastDip = now;
                    // contro un fianco di duna ripido (oltre 12 gradi nei prossimi 3, 6 o 12 m) una frenata o un urto e' fisica: non e' la sabbia piana di cui si lamenta il giocatore
                    float h0 = DesertWorld.Height(pos.x, pos.z);
                    float slope = (DesertWorld.Height(pos.x + fwd.x * 3f, pos.z + fwd.z * 3f) - h0) / 3f, steepest = slope;
                    foreach (float ahead in new[] { 6f, 12f }) steepest = Mathf.Max(steepest, (DesertWorld.Height(pos.x + fwd.x * ahead, pos.z + fwd.z * ahead) - h0) / ahead);
                    bool onSteep = Mathf.Atan(steepest) * Mathf.Rad2Deg > FlatSteepDeg;
                    if (crash) { if (onSteep) st.crashesSteep++; else st.crashes++; }
                    else { if (onSteep) st.dipsSteep++; else st.dips++; }
                    var c = pl.RideLastContact;
                    bool touched = c.collider != null && Time.frameCount - c.frame <= 4;
                    if (touched) st.explained++;
                    if (st.dips + st.crashes + st.dipsSteep + st.crashesSteep <= 8)
                    {
                        // il profilo del suolo davanti (la funzione dell'altezza, rispetto a dove sta il corpo): un fianco ripido si vede qui
                        var prof = new StringBuilder();
                        foreach (float ahead in new[] { 1f, 2f, 3f, 4f, 6f, 8f, 12f }) prof.Append($" {DesertWorld.Height(pos.x + fwd.x * ahead, pos.z + fwd.z * ahead) - pos.y:+0.00;-0.00}");
                        Debug.Log($"[PryingEyes] desertotest motopiano {st.tag}: profilo del suolo davanti a 1, 2, 3, 4, 6, 8, 12 m (quota rispetto ai piedi):{prof}");
                        Debug.Log($"[PryingEyes] desertotest motopiano {st.tag}: {(crash ? "URTO" : "FRENATA")}{(onSteep ? " SU UN FIANCO RIPIDO" : "")} a {now - t0:F1} s, {Flat(pos, origin):F0} m dal via, da {vRecent * 3.6f:F0} a {v * 3.6f:F0} km/h, fotogramma {dt * 1000f:F0} ms, pendenza davanti {Mathf.Atan(slope) * Mathf.Rad2Deg:F1} gradi (la massima nei prossimi 12 m {Mathf.Atan(steepest) * Mathf.Rad2Deg:F1}), a terra {pl.RideGrounded}, terreno davanti pronto {terr == null || terr.Ready(pos + fwd * (Mathf.Abs(v) * 0.9f + 8f), 0f)}; {FlatContact(pl, cc)}; ultimi fotogrammi (passo, comandata>fatta, perdita, accumulo):{FlatTrace(pl)}");
                    }
                }

                // la vita del pilota: si misura quanta ne perde, e si rimette a posto prima che muoia
                float life = pl.hp + pl.shield;
                if (life < lifePrev) st.hpLost += lifePrev - life;
                lifePrev = life;
                if (life < 70f) { pl.hp = 100f; pl.shield = 50f; lifePrev = 150f; }

                if (now - lastClear > 1.5f) { lastClear = now; ClearHorde(); }
                if (steady && now - lastProbe > 0.25f)
                {
                    lastProbe = now; st.probes++;
                    SandCoverage(pos, out float nm, out float nc, out int miss, out int coarse);
                    st.nearestMissing = Mathf.Min(st.nearestMissing, nm); st.nearestCoarse = Mathf.Min(st.nearestCoarse, nc);
                    if (scatter != null) st.nearestCell = Mathf.Min(st.nearestCell, scatter.NearestMissingCell(pos));
                    st.missingMax = Mathf.Max(st.missingMax, miss); st.coarseMax = Mathf.Max(st.coarseMax, coarse);
                    // i luoghi nuovi (oltre 770 m all'inizio): da che distanza si vedono? Quelli che nascono dopo si aggiungono man mano che la sabbia li fa conoscere
                    DesertWorld.SitesNearKnown(pos.x, pos.z, 1500f, popList);
                    foreach (var s in popList) if (!popWatch.ContainsKey(s) && SiteEdge(s, origin) > 770f) popWatch[s] = false;
                    var keys = new List<Site>(popWatch.Keys);
                    foreach (var s in keys)
                        if (!popWatch[s] && SiteVisible(s)) { popWatch[s] = true; st.popDists.Add(SiteEdge(s, pos)); }
                }
                last = pos;
                yield return null;
            }
            st.secs = Time.time - t0;
            st.dist = Flat(last, origin);
            DesertBike.botBoost = false; DesertBike.botThrottle = -1f; DesertBike.botSteer = 0f;
            float tb = Time.time;
            while (pl.Riding && Time.time - tb < 10f && Mathf.Abs(pl.RideSpeed) > 0.5f) yield return null;
            DesertBike.botThrottle = 0f;
        }

        /// <summary>Aspetta che tutta la sabbia attorno ci sia, col dettaglio giusto, e che le code dei lavori (blocchi e cose) siano vuote: si parte da un mondo finito, cosi' quello che manca dopo e' colpa della velocita'.</summary>
        IEnumerator FlatSettle(PlayerController pl)
        {
            float t0 = Time.time;
            int miss = -1, coarse = -1, cells = -1, jobs = -1;
            yield return WaitFor(() =>
            {
                SandCoverage(pl.transform.position, out _, out _, out miss, out coarse);
                var sc = DesertScatter.I; var tr = DesertTerrain.I;
                cells = sc != null ? sc.CellsPending : 0; jobs = tr != null ? tr.PendingJobs : 0;
                // un quadrato di cose sul bordo del raggio piu' largo (520 m) puo' aspettare per sempre: vuole i luoghi attorno a lui (due celle di 140 m) e il terreno li fa solo fin dove arriva la sabbia
                // (circa 900 m). Misurato: manca solo quel bordo (503 m al piu' vicino durante le corse), quindi non si aspetta oltre i 450 m (prima era un'attesa di 120 s a ogni partenza lontana)
                bool propsOk = sc == null || sc.NearestMissingCell(pl.transform.position) > 450f;
                return miss == 0 && coarse == 0 && propsOk && jobs == 0;
            }, 120f);
            Debug.Log($"[PryingEyes] desertotest motopiano: il mondo attorno e' finito dopo {Time.time - t0:F1} s (sabbia mancante {miss}, grossolana {coarse}, quadrati di cose in coda {cells}, lavori {jobs}){(cells > 0 && DesertScatter.I != null ? DesertScatter.I.PendingReport(pl.transform.position) : "")}");
            ClearHorde();           // quelli che si sono avvicinati mentre si aspettava (il pilota e' invulnerabile, ma sarebbero in mezzo alla partenza)
            yield return new WaitForSeconds(0.5f);
        }

        void FlatReport(FlatStats s)
        {
            float meanApply = s.applyFrames > 0 ? s.applyDtSum / s.applyFrames * 1000f : 0f, meanPlain = s.plainFrames > 0 ? s.plainDtSum / s.plainFrames * 1000f : 0f;
            Debug.Log($"[PryingEyes] desertotest motopiano {s.tag}: {(s.reached ? "arrivata" : "NON arrivata")}, {s.dist:F0} m in {s.secs:F0} s (media {s.dist / Mathf.Max(0.1f, s.secs) * 3.6f:F0} km/h, massima {s.vMax * 3.6f:F0}); " +
                $"{s.crashes} urti e {s.dips} frenate da 3 m/s o piu' su sabbia piana (e {s.crashesSteep} + {s.dipsSteep} contro un fianco ripido; {s.explained} con un contatto laterale), vita persa {s.hpLost:F0}; terreno davanti non pronto {s.brakeFrames} fotogrammi ({s.brakeSecs:F2} s di freno); " +
                $"sabbia (dopo i primi 60 m, {s.steadyFrames} fotogrammi, {s.probes} controlli): il blocco mancante piu' vicino a {s.nearestMissing:F0} m (fino a {s.missingMax} insieme), il piu' grossolano a {s.nearestCoarse:F0} m (fino a {s.coarseMax}), lavori in coda al massimo {s.maxPendingJobs}, quadrati di cose in coda al massimo {s.maxPendingCells}, il quadrato di cose mancante piu' vicino a {s.nearestCell:F0} m; " +
                $"luoghi comparsi mentre si correva: {s.popDists.Count} ({PopText(s)}); " +
                $"fotogrammi: {s.frames}, il peggiore {s.worstDt * 1000f:F0} ms, {s.hitches} sopra 100 ms; con blocchi nati {s.applyFrames} fotogrammi di {meanApply:F1} ms in media (max {s.applyDtMax * 1000f:F0}), senza {s.plainFrames} di {meanPlain:F1} ms (max {s.plainDtMax * 1000f:F0})");
        }

        static string PopText(FlatStats s)
        {
            if (s.popDists.Count == 0) return "nessuno";
            var d = new List<float>(s.popDists); d.Sort();
            int late = 0; foreach (float x in d) if (x < 500f) late++;
            return $"visibili da {d[d.Count - 1]:F0} m al piu' presto, {d[d.Count / 2]:F0} m la mediana, {d[0]:F0} m al piu' tardi, {late} sotto i 500 m";
        }

        // ------------------------------------------------------------------ la fase
        IEnumerator MotoFlat()
        {
            var pl = Pl;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset(); Weapon.rideBotFire = false;
            motoWorst = 0f;
            ClearHorde();
            bool dropsWere = Drops.disabled;
            Drops.ResetRun(); Drops.disabled = true;
            DesertBike bike = null;
            yield return WaitFor(() => { bike = FirstBike(); return bike != null; }, 10f);
            Check("motopiano: c'e' una moto", bike != null);
            if (bike == null) { Drops.disabled = dropsWere; yield break; }

            // ---- 1. i collider delle cose contro le forme che si vedono
            int ghosts = PropColliderAudit();
            Check("motopiano: nessuna capsula sporge di piu' di 0,6 m di lato o 0,5 m in alto dalla forma che si disegna", ghosts == 0, $"{ghosts} generi");

            // ---- 2. su sabbia aperta, lontano da tutto (-motofar 5000: a cinque chilometri dall'origine, dove le posizioni hanno meno decimali; -motofps 90: con i fotogrammi di un gioco vero)
            float fps = FlatArg("-motofps", 0f), far = FlatArg("-motofar", 0f);
            if (fps > 0f) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = Mathf.RoundToInt(fps); }
            var open = far > 0f ? OpenSandNear(new Vector2(far, far * 0.7f)) : OpenSand(500f);
            Vector3 spot = new Vector3(open.x, 0f, open.y);
            Debug.Log($"[PryingEyes] desertotest motopiano: partenza vicino a {spot:F0} ({new Vector2(spot.x, spot.z).magnitude:F0} m dall'origine), fotogrammi al secondo imposti {(fps > 0f ? fps.ToString("F0") : "nessun limite")}");
            float heading; float maxSlope;
            var routeA = BestFlatRoute(spot, 40, out heading, out maxSlope);
            float lenA = (routeA.Count - 1) * 64f;
            Debug.Log($"[PryingEyes] desertotest motopiano: rotta A da {spot:F0}, direzione {heading * Mathf.Rad2Deg:F0} gradi, {routeA.Count - 1} tratti ({lenA:F0} m), pendenza massima {Mathf.Atan(maxSlope) * Mathf.Rad2Deg:F1} gradi; candidati scartati per la pendenza {flatFail[0]}, per un luogo vicino {flatFail[1]}, per una cosa solida {flatFail[2]}");
            float minLen = far > 0f ? 800f : 1500f;           // lontano dall'origine la sabbia e' piu' ruvida: basta una rotta piu' corta
            Check($"motopiano: c'e' una rotta sgombra e poco ripida di almeno {(minLen >= 1500f ? "1,5 km" : "800 m")} per la corsa a 83 km/h", lenA >= minLen, $"{lenA:F0} m");
            if (routeA.Count < 3) { Drops.disabled = dropsWere; if (fps > 0f) Application.targetFrameRate = -1; yield break; }

            yield return GoTo(routeA[0].x, routeA[0].z, 2f);
            yield return new WaitForSeconds(1.0f);
            SeamAudit(pl.transform.position, out int seamSamples, out int seamSteps, out float seamWorst, out int seamHoles);
            Check("motopiano: tra i blocchi di sabbia non ci sono gradini oltre 4 cm (oltre il dislivello vero) ne' buchi", seamSamples > 200 && seamSteps == 0 && seamHoles == 0, $"{seamSamples} campioni, {seamSteps} gradini, {seamHoles} buchi, scarto massimo {seamWorst * 100f:F1} cm");

            var stA = new FlatStats { tag = "A a 83 km/h", boost = false };
            Vector3 dirA = routeA[1] - routeA[0]; dirA.y = 0f; float headA = Mathf.Atan2(dirA.x, dirA.z);
            yield return PlowStart(bike, pl.transform.position, headA);
            yield return FlatSettle(pl);
            godMode = false; pl.invulnerable = false;                        // si misura anche il danno al pilota, ma solo da qui: durante l'attesa (fino a due minuti) il pilota e' fermo e le guardie lo raggiungerebbero
            yield return RideFlat(routeA, false, stA);
            FlatReport(stA);
            godMode = true; pl.invulnerable = true;

            // ---- B: dal punto d'arrivo, un'altra rotta, col turbo
            Vector3 stop = pl.transform.position; stop.y = 0f;
            var routeB = BestFlatRoute(stop, 40, out float headingB, out float slopeB);
            float lenB = (routeB.Count - 1) * 64f;
            Debug.Log($"[PryingEyes] desertotest motopiano: rotta B da {stop:F0}, direzione {headingB * Mathf.Rad2Deg:F0} gradi, {routeB.Count - 1} tratti ({lenB:F0} m), pendenza massima {Mathf.Atan(slopeB) * Mathf.Rad2Deg:F1} gradi");
            var stB = new FlatStats { tag = "B a 112 km/h (turbo)", boost = true };
            if (routeB.Count >= 3)
            {
                // la moto riparte ferma, ben orientata, dall'inizio della rotta (girare in sella con il turbo faceva un gran giro che finiva contro un teatro: una prova che sbaglia la partenza, non il gioco)
                if (pl.Riding) pl.AbortRide();
                yield return GoTo(routeB[0].x, routeB[0].z, 2f);
                Vector3 dirB = routeB[1] - routeB[0]; dirB.y = 0f;
                yield return PlowStart(bike, pl.transform.position, Mathf.Atan2(dirB.x, dirB.z));
                yield return FlatSettle(pl);
                godMode = false; pl.invulnerable = false;
                yield return RideFlat(routeB, true, stB);
                FlatReport(stB);
            }
            else Check("motopiano: c'e' una rotta sgombra per la corsa col turbo", false, $"{lenB:F0} m");

            // ---- esiti
            Check("motopiano: la corsa a 83 km/h arriva in fondo alla rotta", stA.reached, $"{stA.dist:F0} m su {lenA:F0}");
            Check("motopiano: la corsa col turbo arriva in fondo alla rotta", stB.reached, $"{stB.dist:F0} m su {lenB:F0}");
            Check("motopiano: su sabbia piana e sgombra nessun urto, a 83 e a 112 km/h", stA.crashes + stB.crashes == 0, $"{stA.crashes} + {stB.crashes} urti (piu' {stA.crashesSteep + stB.crashesSteep} contro fianchi ripidi), vita persa {stA.hpLost + stB.hpLost:F0}");
            Check("motopiano: su sabbia piana e sgombra nessuna frenata da 3 m/s o piu'", stA.dips + stB.dips == 0, $"{stA.dips} + {stB.dips} frenate (piu' {stA.dipsSteep + stB.dipsSteep} contro fianchi ripidi)");
            Check("motopiano: il terreno davanti e' sempre pronto: la moto non frena da sola", stA.brakeFrames + stB.brakeFrames == 0, $"{stA.brakeFrames} + {stB.brakeFrames} fotogrammi, {stA.brakeSecs + stB.brakeSecs:F2} s");
            Check("motopiano: la sabbia vicina (entro 150 m) ha sempre il collider oltre i 100 m", Mathf.Min(stA.nearestCoarse, stB.nearestCoarse) > 100f || Mathf.Min(stA.nearestCoarse, stB.nearestCoarse) >= 9999f, $"il piu' vicino senza collider a {Mathf.Min(stA.nearestCoarse, stB.nearestCoarse):F0} m");
            Check("motopiano: nessun blocco di sabbia manca entro 600 m", Mathf.Min(stA.nearestMissing, stB.nearestMissing) > 600f, $"il piu' vicino mancante a {Mathf.Min(stA.nearestMissing, stB.nearestMissing):F0} m");

            DesertBike.BotReset();
            godMode = true; pl.invulnerable = true;
            Drops.disabled = dropsWere;
            if (fps > 0f) Application.targetFrameRate = -1;
        }

        static float FlatArg(string name, float def)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == name && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) return v;
            return def;
        }

        /// <summary>Come OpenSand, ma a spirale attorno a un punto qualunque (il primo senza luoghi a meno di 75 m).</summary>
        static Vector2 OpenSandNear(Vector2 c)
        {
            var list = new List<Site>();
            for (int k = 0; k < 90; k++)
            {
                float a = (k * 47f + 20f) * Mathf.Deg2Rad, r = 30f + 18f * (k % 7) + 10f * (k / 7);
                float x = c.x + Mathf.Cos(a) * r, z = c.y + Mathf.Sin(a) * r;
                DesertWorld.SitesNear(x, z, 75f, list);
                if (list.Count == 0) return new Vector2(x, z);
            }
            return c;
        }
    }
}
#endif
