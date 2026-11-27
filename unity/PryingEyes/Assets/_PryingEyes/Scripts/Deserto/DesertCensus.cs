#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>La prova dei muri (-desertonly rovine): conta, sui triangoli veri del guscio di un luogo, i difetti che da fuori e da vicino si vedono ad occhio.
    /// 1) Triangoli capovolti: il giro dei vertici non e' quello delle normali (un quadro a farfalla: una fetta di muro il cui bordo alto taglia il basso). Da un lato non si vede.
    /// 2) Facce sovrapposte a filo: due triangoli che guardano dalla stessa parte sullo stesso piano (a meno di 4 mm) e si coprono per piu' di 50 cm2: si contendono i pixel e lampeggiano da vicino.
    /// 3) Buchi: raggi lanciati da fuori verso l'edificio da altezze d'occhio; se la prima superficie che incontrano e' vista da dietro (e quindi non disegnata) da fuori si vede dentro il muro
    ///    o dentro l'edificio. Si fermano ai materiali a due facce. E' lo stesso test che farebbe un occhio, senza GPU: il tempo di una macchina e le foto servono per guardare, non per contare.
    /// Lavora in fette di pochi millisecondi (sono enumeratori), cosi' la prova non blocca il gioco.</summary>
    public sealed class DesertCensus
    {
        readonly List<Vector3> la = new List<Vector3>(), lb = new List<Vector3>(), lc = new List<Vector3>(), lvn = new List<Vector3>();
        readonly List<int> lmat = new List<int>(), pairs = new List<int>();
        readonly List<string> matNames = new List<string>();
        readonly List<bool> matTwo = new List<bool>();
        readonly Dictionary<Material, int> matIds = new Dictionary<Material, int>();
        int nullMat = -1;
        Vector3[] a, b, c, n, vn;
        float[] area;
        int[] mat;

        public int Flipped, Overlaps, OverlapsFacade, Doubles, Hidden, Rays, LeaksWall, LeaksFlat, LeaksPane;
        public float FlippedArea, OverlapArea, DoubleArea, HiddenArea;
        public readonly Dictionary<string, int> LeakMat = new Dictionary<string, int>();
        /// <summary>Le facce sovrapposte per coppia di materiali, normale e altezza (a metri): per capire da che pezzo vengono.</summary>
        public readonly Dictionary<string, int> OverlapPairs = new Dictionary<string, int>();
        /// <summary>Le facce sovrapposte che si vedono, per coppia di materiali (i numeri di palette tolti) e normale, senza l'altezza: per sommarle su tutti i luoghi.</summary>
        public readonly Dictionary<string, int> OverlapKinds = new Dictionary<string, int>();

        static readonly System.Text.RegularExpressions.Regex PaletteNumber = new System.Text.RegularExpressions.Regex(@"_f\d+");

        string KindOf(int i, int j)
        {
            string p = PaletteNumber.Replace(matNames[mat[i]], "_fN"), q = PaletteNumber.Replace(matNames[mat[j]], "_fN");
            if (string.CompareOrdinal(p, q) > 0) { var t = p; p = q; q = t; }
            return $"{p} / {q} n=({n[i].x:F0},{n[i].y:F0},{n[i].z:F0})";
        }
        public readonly List<string> Samples = new List<string>();
        readonly Dictionary<string, int> noted = new Dictionary<string, int>();

        public int Tris => la.Count;

        // ------------------------------------------------------------------ i triangoli
        /// <summary>Tutte le mesh sotto 'root' (in coordinate del mondo), una per sottomesh: posizioni, normali dei vertici, materiale.</summary>
        public void Add(GameObject root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                var mesh = mf.sharedMesh;
                if (mesh == null || mr == null || !mr.enabled) continue;
                var m = mf.transform.localToWorldMatrix;
                var verts = mesh.vertices; var nrms = mesh.normals;
                bool hasN = nrms != null && nrms.Length == verts.Length;
                var mats = mr.sharedMaterials;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var t = mesh.GetTriangles(sm);
                    int id = MatId(sm < mats.Length ? mats[sm] : null);
                    int first = la.Count;
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        int i0 = t[i], i1 = t[i + 1], i2 = t[i + 2];
                        la.Add(m.MultiplyPoint3x4(verts[i0])); lb.Add(m.MultiplyPoint3x4(verts[i1])); lc.Add(m.MultiplyPoint3x4(verts[i2]));
                        Vector3 avg = hasN ? m.MultiplyVector(nrms[i0] + nrms[i1] + nrms[i2]) : Vector3.zero;
                        lvn.Add(avg.sqrMagnitude > 1e-12f ? avg.normalized : Vector3.zero);
                        lmat.Add(id);
                    }
                    // i quadri di MeshBuilder: due triangoli (i, i+1, i+2) e (i, i+2, i+3) di fila
                    if (t.Length % 6 == 0)
                        for (int k = 0; k < t.Length / 6; k++)
                            if (t[6 * k] == t[6 * k + 3] && t[6 * k + 2] == t[6 * k + 4]) pairs.Add(first + 2 * k);
                }
            }
        }

        int MatId(Material m)
        {
            if (m == null)
            {
                if (nullMat < 0) { nullMat = matNames.Count; matNames.Add("(nessun materiale)"); matTwo.Add(false); }
                return nullMat;
            }
            if (matIds.TryGetValue(m, out int id)) return id;
            id = matNames.Count;
            matIds[m] = id;
            matNames.Add(m.name);
            matTwo.Add(m.HasProperty("_Cull") && m.GetFloat("_Cull") < 0.5f);          // Cull Off: si vede da tutte e due le parti
            return id;
        }

        void Freeze()
        {
            if (a != null && a.Length == la.Count) return;
            int k = la.Count;
            a = la.ToArray(); b = lb.ToArray(); c = lc.ToArray(); vn = lvn.ToArray(); mat = lmat.ToArray();
            n = new Vector3[k]; area = new float[k];
            for (int i = 0; i < k; i++)
            {
                var g = Vector3.Cross(b[i] - a[i], c[i] - a[i]);
                float len = g.magnitude;
                area[i] = len * 0.5f;
                n[i] = len > 1e-9f ? g / len : Vector3.zero;
            }
        }

        static string V3(Vector3 p) => $"({p.x:F2},{p.y:F2},{p.z:F2})";

        void Note(string kind, Vector3 at, int tri, string extra = "")
        {
            noted.TryGetValue(kind, out int count);
            noted[kind] = count + 1;
            if (count >= 3) return;
            Samples.Add($"{kind} a ({at.x:F1}, {at.y:F1}, {at.z:F1}), {matNames[mat[tri]]}, normale ({n[tri].x:F1}, {n[tri].y:F1}, {n[tri].z:F1}){extra}");
        }

        // ------------------------------------------------------------------ 1. triangoli capovolti
        public void CountFlips()
        {
            Freeze();
            for (int i = 0; i < a.Length; i++)
            {
                if (area[i] < 1e-4f || vn[i] == Vector3.zero) continue;
                if (Vector3.Dot(n[i], vn[i]) < -0.1f)
                {
                    Flipped++; FlippedArea += area[i];
                    Note("capovolto", (a[i] + b[i] + c[i]) / 3f, i, $", {area[i] * 10000f:F0} cm2");
                }
            }
        }

        // ------------------------------------------------------------------ 2. facce sovrapposte a filo
        public IEnumerator CountOverlaps()
        {
            Freeze();
            if (cells == null) BuildGrid();
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < a.Length; i++)
            {
                if (area[i] < 0.005f) continue;               // sotto 50 cm2 sono rifiniture
                var q = n[i];
                int key = (Mathf.RoundToInt(q.x * 50f) + 100) | ((Mathf.RoundToInt(q.y * 50f) + 100) << 8) | ((Mathf.RoundToInt(q.z * 50f) + 100) << 16);
                if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<int>();
                l.Add(i);
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var kv in groups)
            {
                var l = kv.Value;
                if (l.Count < 2) continue;
                var offs = new float[l.Count];
                var order = new int[l.Count];
                for (int x = 0; x < l.Count; x++) { order[x] = x; offs[x] = Vector3.Dot(n[l[x]], a[l[x]]); }
                Array.Sort(offs, order);
                for (int x = 0; x < l.Count; x++)
                {
                    int i = l[order[x]];
                    for (int y = x + 1; y < l.Count; y++)
                    {
                        if (offs[y] - offs[x] > 0.004f) break;
                        int j = l[order[y]];
                        float ov = OverlapArea2D(i, j, out Vector3 at);
                        if (ov > 0.005f)
                        {
                            // lo stesso materiale con la proiezione a scatola sul mondo da' gli stessi pixel: due facce uguali una sull'altra non lampeggiano (due parapetti agli spigoli). Lampeggia
                            // un materiale diverso sullo stesso piano (una cornice sul muro): ed e' quello che si conta
                            if (mat[i] == mat[j]) { Doubles++; DoubleArea += ov; continue; }
                            // ...se qualcuno le vede: le facce rivolte dentro il muro (il retro di due cornici appoggiate alla pelle) o dentro un solaio hanno davanti la pelle o il soffitto e non si disegnano mai
                            if (!SeenFrom(at, n[i], out string seenWhy)) { Hidden++; HiddenArea += ov; continue; }
                            Overlaps++; OverlapArea += ov;
                            if (PaletteNumber.IsMatch(matNames[mat[i]]) || PaletteNumber.IsMatch(matNames[mat[j]])) OverlapsFacade++;          // almeno una delle due e' pelle, cornice o zoccolo di una facciata
                            OverlapKinds.TryGetValue(KindOf(i, j), out int kc); OverlapKinds[KindOf(i, j)] = kc + 1;
                            string pair = $"{matNames[mat[i]]} / {matNames[mat[j]]} n=({n[i].x:F0},{n[i].y:F0},{n[i].z:F0}) y={Mathf.Floor((a[i].y + b[i].y + c[i].y) / 3f * 2f) / 2f:F1}";
                            OverlapPairs.TryGetValue(pair, out int pc); OverlapPairs[pair] = pc + 1;
                            Note("sovrapposto", (a[i] + b[i] + c[i]) / 3f, i, $", {ov * 10000f:F0} cm2, con {matNames[mat[j]]}: {V3(a[i])}{V3(b[i])}{V3(c[i])} e {V3(a[j])}{V3(b[j])}{V3(c[j])}, {seenWhy}");
                        }
                    }
                }
                if (sw.ElapsedMilliseconds > 30) { yield return null; sw.Restart(); }
            }
        }

        static readonly float[] SeenDistances = { 0.5f, 1.5f, 4f };

        /// <summary>Una faccia di normale 'nn' nel punto 'at' si vede da qualche parte se, da mezzo metro, un metro e mezzo o quattro metri davanti, la prima faccia disegnata lungo la perpendicolare e' proprio quella
        /// (a un centimetro dal piano). Il retro di una cornice sulla pelle di un muro ha davanti, a 30 cm, la faccia interna del muro: non la vede nessuno.</summary>
        bool SeenFrom(Vector3 at, Vector3 nn, out string why)
        {
            why = "";
            foreach (float dist in SeenDistances)
            {
                Vector3 eye = at + nn * dist;
                int h = FirstHit(eye, -nn, out float t, true);
                if (h >= 0 && Mathf.Abs(t - dist) < 0.01f)
                {
                    // il punto da cui si guarda dev'essere aria: nell'intercapedine di un muro (due facce a 30 cm l'una dall'altra) si 'vede' il retro di qualunque pezzo, ma nessuno ci sta
                    if (InSolid(eye)) { why += $" {dist:F1} m: dentro un muro;"; continue; }
                    why = $"visto da {dist:F1} m";
                    return true;
                }
                why += $" {dist:F1} m: {(h >= 0 ? matNames[mat[h]] + " a " + t.ToString("F2") : "niente")};";
            }
            return false;
        }

        static readonly Vector3[] SixWays = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        /// <summary>Un punto e' dentro un pieno (l'intercapedine di un muro, un parallelepipedo) se, nelle sei direzioni degli assi, almeno due volte la prima faccia che incontra gli da' le spalle.</summary>
        bool InSolid(Vector3 p)
        {
            int back = 0;
            foreach (var d in SixWays)
            {
                int h = FirstHit(p, d, out float t, false);
                if (h >= 0 && !matTwo[mat[h]] && Vector3.Dot(d, n[h]) > 0f) back++;
            }
            return back >= 2;
        }

        /// <summary>L'area in cui due triangoli (quasi sullo stesso piano) si coprono, in m2, e un punto di quella zona, in coordinate del mondo: si proiettano sul piano e si ritaglia uno con l'altro.</summary>
        float OverlapArea2D(int i, int j, out Vector3 at)
        {
            at = Vector3.zero;
            var nn = n[i];
            Vector3 u = Vector3.Cross(nn, Mathf.Abs(nn.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(nn, u);
            Vector2 P(Vector3 p) => new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v));
            var subject = new List<Vector2> { P(a[i]), P(b[i]), P(c[i]) };
            var clip = new List<Vector2> { P(a[j]), P(b[j]), P(c[j]) };
            if (Signed(subject) < 0f) subject.Reverse();
            if (Signed(clip) < 0f) clip.Reverse();
            for (int e = 0; e < 3 && subject.Count > 0; e++)
            {
                Vector2 e0 = clip[e], e1 = clip[(e + 1) % 3];
                var next = new List<Vector2>();
                for (int k = 0; k < subject.Count; k++)
                {
                    Vector2 s = subject[k], t = subject[(k + 1) % subject.Count];
                    float ds = Cross2(e0, e1, s), dt = Cross2(e0, e1, t);
                    if (ds >= 0f) next.Add(s);
                    if ((ds >= 0f) != (dt >= 0f)) next.Add(s + (t - s) * (ds / (ds - dt)));
                }
                subject = next;
            }
            if (subject.Count < 3) return 0f;
            Vector2 g = Vector2.zero;
            foreach (var p in subject) g += p;
            g /= subject.Count;                          // l'intersezione di due triangoli e' convessa: la media dei vertici sta dentro
            at = u * g.x + v * g.y + nn * Vector3.Dot(nn, a[i]);
            return Mathf.Abs(Signed(subject));
        }

        static float Cross2(Vector2 o, Vector2 p, Vector2 q) => (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);

        static float Signed(List<Vector2> poly)
        {
            float s = 0f;
            for (int k = 0; k < poly.Count; k++) { var p = poly[k]; var q = poly[(k + 1) % poly.Count]; s += p.x * q.y - q.x * p.y; }
            return s * 0.5f;
        }

        // ------------------------------------------------------------------ 3. buchi: raggi da fuori
        float cell = 2.5f, gx0, gz0;
        int gw, gh, stampId;
        List<int>[] cells;
        int[] stamp;
        Vector3 boundsMin, boundsMax;

        void BuildGrid()
        {
            Freeze();
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            for (int i = 0; i < a.Length; i++)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = k == 0 ? a[i] : k == 1 ? b[i] : c[i];
                    if (p.x < x0) x0 = p.x; if (p.x > x1) x1 = p.x;
                    if (p.z < z0) z0 = p.z; if (p.z > z1) z1 = p.z;
                    if (p.y < y0) y0 = p.y; if (p.y > y1) y1 = p.y;
                }
            boundsMin = new Vector3(x0, y0, z0); boundsMax = new Vector3(x1, y1, z1);
            gx0 = x0 - 0.5f; gz0 = z0 - 0.5f;
            gw = Mathf.Max(1, Mathf.CeilToInt((x1 - gx0 + 0.5f) / cell)); gh = Mathf.Max(1, Mathf.CeilToInt((z1 - gz0 + 0.5f) / cell));
            cells = new List<int>[gw * gh];
            for (int i = 0; i < a.Length; i++)
            {
                float tx0 = Mathf.Min(a[i].x, Mathf.Min(b[i].x, c[i].x)), tx1 = Mathf.Max(a[i].x, Mathf.Max(b[i].x, c[i].x));
                float tz0 = Mathf.Min(a[i].z, Mathf.Min(b[i].z, c[i].z)), tz1 = Mathf.Max(a[i].z, Mathf.Max(b[i].z, c[i].z));
                int cx0 = Mathf.Clamp((int)((tx0 - gx0) / cell), 0, gw - 1), cx1 = Mathf.Clamp((int)((tx1 - gx0) / cell), 0, gw - 1);
                int cz0 = Mathf.Clamp((int)((tz0 - gz0) / cell), 0, gh - 1), cz1 = Mathf.Clamp((int)((tz1 - gz0) / cell), 0, gh - 1);
                for (int cz = cz0; cz <= cz1; cz++)
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        var l = cells[cz * gw + cx];
                        if (l == null) cells[cz * gw + cx] = l = new List<int>();
                        l.Add(i);
                    }
            }
            stamp = new int[a.Length];
        }

        static bool Clip1D(float o, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-8f) return o >= lo && o <= hi;
            float s = (lo - o) / d, e = (hi - o) / d;
            if (s > e) { float tmp = s; s = e; e = tmp; }
            if (s > t0) t0 = s;
            if (e < t1) t1 = e;
            return t0 <= t1;
        }

        bool RayTri(int i, Vector3 o, Vector3 d, out float t)
        {
            t = 0f;
            Vector3 e1 = b[i] - a[i], e2 = c[i] - a[i];
            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-10f) return false;
            float inv = 1f / det;
            Vector3 s = o - a[i];
            float u = Vector3.Dot(s, p) * inv;
            if (u < -1e-5f || u > 1f + 1e-5f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < -1e-5f || u + v > 1f + 1e-5f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return true;
        }

        /// <summary>Il primo triangolo che il raggio incontra. -1 se non ne incontra. Con 'frontOnly' conta solo quelli che l'occhio disegna (di fronte, o a due facce): e' quello che si vede.</summary>
        int FirstHit(Vector3 o, Vector3 d, out float tHit, bool frontOnly = false, bool wallsOnly = false)
        {
            tHit = float.MaxValue;
            float tEnter = 0f, tLeave = float.MaxValue;
            if (!Clip1D(o.x, d.x, gx0, gx0 + gw * cell, ref tEnter, ref tLeave)) return -1;
            if (!Clip1D(o.z, d.z, gz0, gz0 + gh * cell, ref tEnter, ref tLeave)) return -1;
            float px = o.x + d.x * tEnter, pz = o.z + d.z * tEnter;
            int cx = Mathf.Clamp((int)((px - gx0) / cell), 0, gw - 1), cz = Mathf.Clamp((int)((pz - gz0) / cell), 0, gh - 1);
            int sx = d.x > 0f ? 1 : -1, sz = d.z > 0f ? 1 : -1;
            float tdx = Mathf.Abs(d.x) > 1e-8f ? cell / Mathf.Abs(d.x) : float.MaxValue, tdz = Mathf.Abs(d.z) > 1e-8f ? cell / Mathf.Abs(d.z) : float.MaxValue;
            float tmx = Mathf.Abs(d.x) > 1e-8f ? (gx0 + (cx + (sx > 0 ? 1 : 0)) * cell - o.x) / d.x : float.MaxValue;
            float tmz = Mathf.Abs(d.z) > 1e-8f ? (gz0 + (cz + (sz > 0 ? 1 : 0)) * cell - o.z) / d.z : float.MaxValue;
            stampId++;
            int best = -1;
            while (true)
            {
                var list = cells[cz * gw + cx];
                if (list != null)
                    for (int k = 0; k < list.Count; k++)
                    {
                        int ti = list[k];
                        if (stamp[ti] == stampId) continue;
                        stamp[ti] = stampId;
                        if (wallsOnly && (Mathf.Abs(n[ti].y) > 0.3f || Mathf.Abs(Vector3.Dot(d, n[ti])) < 0.30f)) continue;
                        if (frontOnly && !matTwo[mat[ti]] && Vector3.Dot(d, n[ti]) >= 0f) continue;
                        if (RayTri(ti, o, d, out float t) && t > 1e-3f && t < tHit) { tHit = t; best = ti; }
                    }
                float tExit = Mathf.Min(tmx, tmz);
                if (best >= 0 && tHit <= tExit) break;
                if (tExit > tLeave) break;
                if (tmx < tmz) { cx += sx; if (cx < 0 || cx >= gw) break; tmx += tdx; }
                else { cz += sz; if (cz < 0 || cz >= gh) break; tmz += tdz; }
            }
            return best;
        }

        // The rendered shell is the reference, independent of the physics collider and its backface settings.
        public bool CrossesWall(Vector3 from, Vector3 to, out Vector3 point)
        {
            point = default;
            Vector3 travel = to - from;
            float distance = travel.magnitude;
            if (Tris == 0 || distance < 0.007f) return false;
            if (cells == null) BuildGrid();
            Vector3 direction = travel / distance;
            if (FirstHit(from, direction, out float hit, wallsOnly: true) < 0 || hit >= distance - 0.002f) return false;
            point = from + direction * hit;
            return true;
        }

        /// <summary>Lancia 'rays' raggi da fuori verso l'edificio: l'origine a un'altezza d'occhio (o piu' in alto, un decimo dei raggi: chi guarda da una duna o da una torre) sopra il suolo, a una distanza
        /// dal bordo della pianta tra 3 e 15 m (un quarto dei raggi da piu' lontano, fino a 60 m), il bersaglio un punto qualunque della pianta a una quota qualunque fino alla cima. Un raggio e' un buco
        /// se la prima superficie che incontra e' vista da dietro: da fuori quella faccia non si disegna e il raggio, per l'occhio, passa.</summary>
        public IEnumerator ShootLeaks(System.Random rng, int rays, float groundY, float eyeHigh)
        {
            if (cells == null) BuildGrid();
            float cx = (boundsMin.x + boundsMax.x) * 0.5f, cz = (boundsMin.z + boundsMax.z) * 0.5f;
            float hx = (boundsMax.x - boundsMin.x) * 0.5f, hz = (boundsMax.z - boundsMin.z) * 0.5f;
            float rad = Mathf.Sqrt(hx * hx + hz * hz);
            float top = Mathf.Max(boundsMax.y, groundY + 1f);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < rays; k++)
            {
                double ang = rng.NextDouble() * Math.PI * 2.0;
                float dist = rad + 3f + (float)rng.NextDouble() * (k % 4 == 0 ? 57f : 12f);
                float eye = 1.7f + (k % 10 == 0 ? (float)rng.NextDouble() * eyeHigh : (float)rng.NextDouble() * 0.6f);
                var o = new Vector3(cx + (float)Math.Cos(ang) * dist, groundY + eye, cz + (float)Math.Sin(ang) * dist);
                var target = new Vector3(Mathf.Lerp(boundsMin.x, boundsMax.x, (float)rng.NextDouble()), groundY + 0.2f + (float)rng.NextDouble() * Mathf.Max(0.5f, top - groundY - 0.2f),
                                         Mathf.Lerp(boundsMin.z, boundsMax.z, (float)rng.NextDouble()));
                Vector3 d = target - o;
                if (d.sqrMagnitude < 1e-6f) continue;
                d.Normalize();
                Rays++;
                int hit = FirstHit(o, d, out float th);
                if (hit < 0) continue;
                if (matTwo[mat[hit]] || Vector3.Dot(d, n[hit]) < 0f) continue;          // si vede (o e' a due facce)
                // un foglio visto da dietro con lo stesso foglio disegnato a filo dall'altra parte (il sotto di un tetto, un vetro a due facce) non e' un buco: l'occhio vede la faccia a filo
                if (FirstHit(o, d, out float tSeen, true) >= 0 && tSeen <= th + 0.004f) continue;
                bool wall = Mathf.Abs(n[hit].y) < 0.5f;
                string nm = matNames[mat[hit]];
                if (IsPane(nm)) { LeaksPane++; continue; }          // un vetro visto da fuori: e' una finestra che non si disegna, si conta a parte
                if (wall) LeaksWall++; else LeaksFlat++;
                LeakMat.TryGetValue(nm, out int cnt);
                LeakMat[nm] = cnt + 1;
                Note(wall ? "buco in un muro" : "buco in un piano", o + d * th, hit, $", visto da ({o.x:F1}, {o.y:F1}, {o.z:F1})");
                if (sw.ElapsedMilliseconds > 30) { yield return null; sw.Restart(); }
            }
        }

        /// <summary>Le 'k' coppie piu' numerose di facce sovrapposte, con quante sono.</summary>
        public List<string> TopOverlapPairs(int k)
        {
            var l = new List<KeyValuePair<string, int>>(OverlapPairs);
            l.Sort((p, q) => q.Value.CompareTo(p.Value));
            var res = new List<string>();
            for (int i = 0; i < l.Count && i < k; i++) res.Add($"{l[i].Value} x {l[i].Key}");
            return res;
        }

        public string Summary()
        {
            var mats = new List<string>();
            foreach (var kv in LeakMat) mats.Add($"{kv.Key} {kv.Value}");
            return $"{Tris} triangoli; capovolti {Flipped} ({FlippedArea:F2} m2); sovrapposti a filo {Overlaps} ({OverlapsFacade} di facciata, {OverlapArea:F2} m2; innocue: {Doubles} uguali e {Hidden} nascoste); buchi {LeaksWall + LeaksFlat} su {Rays} raggi (muri {LeaksWall}, piani {LeaksFlat}), vetri visti da dietro {LeaksPane}"
                + (mats.Count > 0 ? " [" + string.Join(", ", mats) + "]" : "");
        }

        // ------------------------------------------------------------------ 4. l'immagine: quello che vedrebbe un occhio, disegnato dalla CPU (senza GPU)
        static bool IsPane(string name)
        {
            string s = name.ToLowerInvariant();
            return s.Contains("glass") || s.Contains("vetr");
        }

        static readonly System.Text.RegularExpressions.Regex SkinName = new System.Text.RegularExpressions.Regex(@"_f\d+");

        /// <summary>Un colore per classe di materiale, per leggere l'immagine: vetri azzurri, tetti scuri, interni arancio (quello che da fuori non si deve vedere), pelle dei muri e pietra beige.</summary>
        static Color ClassColor(string name)
        {
            string s = name.ToLowerInvariant();
            if (IsPane(s)) return new Color(0.20f, 0.45f, 0.75f);
            if (s.Contains("ardesia") || s.Contains("piombo") || s.Contains("catrama")) return new Color(0.26f, 0.27f, 0.30f);
            if (SkinName.IsMatch(s)) return s.Contains("cornice") ? new Color(0.90f, 0.88f, 0.82f) : new Color(0.74f, 0.70f, 0.60f);
            if (s.StartsWith("kit_") || s == "wall" || s == "wood" || s == "ceil" || s == "floor" || s == "carpet") return new Color(0.92f, 0.48f, 0.14f);
            return new Color(0.62f, 0.62f, 0.64f);
        }

        public Vector3 Centre { get { if (cells == null) BuildGrid(); return new Vector3((boundsMin.x + boundsMax.x) * 0.5f, 0f, (boundsMin.z + boundsMax.z) * 0.5f); } }
        public float Radius { get { if (cells == null) BuildGrid(); return Mathf.Sqrt(Mathf.Pow(boundsMax.x - boundsMin.x, 2f) + Mathf.Pow(boundsMax.z - boundsMin.z, 2f)) * 0.5f; } }
        public float TopY { get { if (cells == null) BuildGrid(); return boundsMax.y; } }

        /// <summary>Un'immagine PNG di quello che si vede da 'eye' guardando 'look' (solo le facce che l'occhio disegna). Dove il primo triangolo e' visto da dietro (e quindi non si disegna) l'immagine
        /// e' colorata: giallo se e' un vetro, magenta se e' un muro: li' da fuori si vede dentro.</summary>
        int magentaSeen, magentaNoted;

        public IEnumerator Render(string path, Vector3 eye, Vector3 look, int w, int h, float fovDeg, float groundY)
        {
            if (cells == null) BuildGrid();
            magentaSeen = 0;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            Vector3 fwd = (look - eye).normalized, right = Vector3.Cross(Vector3.up, fwd).normalized, up = Vector3.Cross(fwd, right);
            float tanH = Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad), aspect = (float)w / h;
            var sun = new Vector3(0.45f, 0.80f, 0.40f).normalized;
            var sandCol = new Color(0.78f, 0.68f, 0.50f);
            var px = new Color32[w * h];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float sx = (2f * (x + 0.5f) / w - 1f) * tanH * aspect, sy = (2f * (y + 0.5f) / h - 1f) * tanH;
                    Vector3 d = (fwd + right * sx + up * sy).normalized;
                    int any = FirstHit(eye, d, out float ta, false);
                    int vis; float tv;
                    if (any >= 0 && (matTwo[mat[any]] || Vector3.Dot(d, n[any]) < 0f)) { vis = any; tv = ta; }          // il primo e' di fronte: e' quello che si vede
                    else if (any < 0) { vis = -1; tv = float.MaxValue; }
                    else vis = FirstHit(eye, d, out tv, true);
                    float tg = d.y < -1e-4f ? (groundY - eye.y) / d.y : float.MaxValue;
                    Color sky = Color.Lerp(new Color(0.80f, 0.86f, 0.92f), new Color(0.45f, 0.62f, 0.90f), Mathf.Clamp01(d.y * 2.2f));
                    Color col;
                    float tSeen;
                    if (vis >= 0 && tv < tg)
                    {
                        Vector3 nr = n[vis]; if (Vector3.Dot(nr, d) > 0f) nr = -nr;
                        col = ClassColor(matNames[mat[vis]]) * (0.38f + 0.62f * Mathf.Clamp01(Vector3.Dot(nr, sun)));
                        tSeen = tv;
                    }
                    else if (tg < float.MaxValue) { col = sandCol * (0.55f + 0.45f * Mathf.Clamp01(d.y * -3f + 0.3f)); tSeen = tg; }
                    else { col = sky; tSeen = float.MaxValue; }
                    if (tSeen < float.MaxValue) col = Color.Lerp(col, sky, 1f - Mathf.Exp(-tSeen / 220f));
                    if (any >= 0 && ta < tSeen - 1e-3f && !matTwo[mat[any]] && Vector3.Dot(d, n[any]) >= 0f)
                    {
                        col = Color.Lerp(col, IsPane(matNames[mat[any]]) ? new Color(1f, 0.9f, 0.1f) : new Color(1f, 0f, 1f), 0.7f);
                        if (++magentaSeen % 400 == 1 && magentaNoted++ < 8)
                            Samples.Add($"magenta a pixel ({x},{y}): prima faccia {matNames[mat[any]]} n=({n[any].x:F1},{n[any].y:F1},{n[any].z:F1}) a {ta:F2} m in {V3(eye + d * ta)} ({V3(a[any])}{V3(b[any])}{V3(c[any])}), poi si vede {(vis >= 0 ? matNames[mat[vis]] + " a " + tv.ToString("F2") : "il cielo o la sabbia")}");
                    }
                    px[y * w + x] = col;
                }
                if (sw.ElapsedMilliseconds > 30) { yield return null; sw.Restart(); }
            }
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
#endif
