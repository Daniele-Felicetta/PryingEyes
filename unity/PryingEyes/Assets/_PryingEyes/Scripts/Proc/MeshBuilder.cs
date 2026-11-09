using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Un'apertura (porta, arcata, finestra) vista da una faccia di muro: centro e semiluce lungo il muro, in metri.</summary>
    public struct Hole
    {
        public float c, hw;
        public Opening o;
    }

    /// <summary>Costruisce mesh a runtime, una sottomesh per materiale. Unity: faccia anteriore = giro orario visto da
    /// davanti, normale = Cross(b-a, c-a). Tutte le funzioni prendono la direzione verso cui la faccia deve guardare e
    /// riordinano i vertici da sole. UV in metri (le texture hanno il tiling nel materiale).</summary>
    public sealed class MeshBuilder
    {
        public sealed class Sub
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
        }

        readonly Dictionary<Material, Sub> subs = new Dictionary<Material, Sub>();
        readonly List<Material> order = new List<Material>();

        public Sub S(Material m)
        {
            if (!subs.TryGetValue(m, out var s)) { s = new Sub(); subs[m] = s; order.Add(m); }
            return s;
        }

        public bool Empty => order.Count == 0;

        // ---------------------------------------------------------------- primitive
        /// <summary>Quadrilatero a-b-c-d (in giro) con UV, rivolto verso 'facing'. Normale piatta.</summary>
        public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 facing)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(c - a, d - a);
            if (n.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(n, facing) < 0f)
            {
                // inverto il giro: a d c b
                var tv = b; b = d; d = tv;
                var tu = ub; ub = ud; ud = tu;
                n = -n;
            }
            n.Normalize();
            var s = S(m);
            int i = s.v.Count;
            s.v.Add(a); s.v.Add(b); s.v.Add(c); s.v.Add(d);
            s.n.Add(n); s.n.Add(n); s.n.Add(n); s.n.Add(n);
            s.uv.Add(ua); s.uv.Add(ub); s.uv.Add(uc); s.uv.Add(ud);
            s.t.Add(i); s.t.Add(i + 1); s.t.Add(i + 2);
            s.t.Add(i); s.t.Add(i + 2); s.t.Add(i + 3);
        }

        /// <summary>Quadrilatero con normali per vertice (superfici curve: volte, archi). L'ordine dei vertici dev'essere
        /// gia' quello giusto: a-b-c-d visto da davanti in senso orario.</summary>
        public void QuadSmooth(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd,
                               Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(c - a, d - a);
            if (n.sqrMagnitude < 1e-12f) return;
            // l'ordine puo' essere sbagliato: lo si corregge guardando le normali date
            if (Vector3.Dot(n, na + nb + nc + nd) < 0f)
            {
                var tv = b; b = d; d = tv;
                var tn = nb; nb = nd; nd = tn;
                var tu = ub; ub = ud; ud = tu;
            }
            var s = S(m);
            int i = s.v.Count;
            s.v.Add(a); s.v.Add(b); s.v.Add(c); s.v.Add(d);
            s.n.Add(na); s.n.Add(nb); s.n.Add(nc); s.n.Add(nd);
            s.uv.Add(ua); s.uv.Add(ub); s.uv.Add(uc); s.uv.Add(ud);
            s.t.Add(i); s.t.Add(i + 1); s.t.Add(i + 2);
            s.t.Add(i); s.t.Add(i + 2); s.t.Add(i + 3);
        }

        /// <summary>UV in metri con proiezione a scatola in coordinate del mondo (come MetricUV dell'albergo).</summary>
        public static Vector2 BoxUV(Vector3 p, Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ay >= ax && ay >= az) return new Vector2(p.x, p.z);
            if (ax >= az) return new Vector2(n.x > 0 ? p.z : -p.z, p.y);
            return new Vector2(n.z > 0 ? -p.x : p.x, p.y);
        }

        public void QuadW(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(n, facing) < 0f) n = -n;
            Quad(m, a, b, c, d, BoxUV(a, n), BoxUV(b, n), BoxUV(c, n), BoxUV(d, n), facing);
        }

        /// <summary>Parallelepipedo orientato (6 facce, o 5 senza 'top'), UV a scatola. 'axisY' ruota attorno a Y.</summary>
        public void Box(Material m, Vector3 center, Vector3 size, float yaw = 0f, bool top = true, bool bottom = true)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 h = size * 0.5f;
            Vector3 P(float sx, float sy, float sz) => center + q * new Vector3(sx * h.x, sy * h.y, sz * h.z);
            Vector3 Nrm(Vector3 d) => q * d;
            // +X -X +Y -Y +Z -Z
            QuadW(m, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Nrm(Vector3.right));
            QuadW(m, P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), Nrm(Vector3.left));
            if (top) QuadW(m, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), Vector3.up);
            if (bottom) QuadW(m, P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), Vector3.down);
            QuadW(m, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Nrm(Vector3.forward));
            QuadW(m, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), Nrm(Vector3.back));
        }

        /// <summary>Trave a sezione rettangolare tra due punti (testa e coda piatte). 'up' orienta la sezione.</summary>
        public void Beam(Material m, Vector3 a, Vector3 b, float wide, float high, Vector3 up)
        {
            Vector3 d = (b - a);
            float len = d.magnitude;
            if (len < 1e-4f) return;
            d /= len;
            Vector3 side = Vector3.Cross(up, d);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(Vector3.right, d);
            side.Normalize();
            Vector3 u2 = Vector3.Cross(d, side).normalized;
            Vector3 sw = side * (wide * 0.5f), uh = u2 * (high * 0.5f);
            Vector3 a0 = a - sw - uh, a1 = a + sw - uh, a2 = a + sw + uh, a3 = a - sw + uh;
            Vector3 b0 = b - sw - uh, b1 = b + sw - uh, b2 = b + sw + uh, b3 = b - sw + uh;
            QuadW(m, a0, a1, b1, b0, -u2);
            QuadW(m, a3, a2, b2, b3, u2);
            QuadW(m, a0, a3, b3, b0, -side);
            QuadW(m, a1, a2, b2, b1, side);
            QuadW(m, a0, a1, a2, a3, -d);
            QuadW(m, b0, b1, b2, b3, d);
        }

        // ---------------------------------------------------------------- muri
        /// <summary>Faccia di muro tra yMin e top(x), con aperture. Parte da 'origin' lungo 'u' (orizzontale, unitario) per
        /// 'len' metri; guarda verso 'facing'. Le aperture sono fette verticali: sopra e sotto di ognuna resta il muro,
        /// la testa puo' essere un arco acuto. 'breaks' sono ascisse in cui 'top' cambia pendenza.</summary>
        public void WallFace(Material m, Vector3 origin, Vector3 u, Vector3 facing, float len, float yMin, Func<float, float> top,
                             List<Hole> holes, float uOff = 0f, float[] breaks = null)
        {
            var xs = new List<float> { 0f, len };
            if (breaks != null) foreach (var b in breaks) if (b > 0.001f && b < len - 0.001f) xs.Add(b);
            if (holes != null)
                foreach (var h in holes)
                {
                    float x0 = Mathf.Max(0f, h.c - h.hw), x1 = Mathf.Min(len, h.c + h.hw);
                    xs.Add(x0); xs.Add(x1);
                    if (h.o.Pointed)
                    {
                        int n = Mathf.Max(6, Mathf.CeilToInt((x1 - x0) / 0.18f));
                        for (int i = 1; i < n; i++) xs.Add(x0 + (x1 - x0) * i / n);
                    }
                }
            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                float xa = xs[i], xb = xs[i + 1];
                if (xb - xa < 0.002f) continue;
                float mid = (xa + xb) * 0.5f;
                float ta = top(xa), tb = top(xb);
                Hole? hit = null;
                if (holes != null)
                    foreach (var h in holes) if (mid > h.c - h.hw && mid < h.c + h.hw) { hit = h; break; }
                if (hit == null) { Slab(m, origin, u, facing, xa, xb, yMin, yMin, ta, tb, uOff); continue; }
                var hl = hit.Value;
                float sill = hl.o.sill;
                float ha = hl.o.Head(xa - hl.c, hl.hw), hb = hl.o.Head(xb - hl.c, hl.hw);
                // sotto la soglia
                float la = Mathf.Min(sill, ta), lb = Mathf.Min(sill, tb);
                if (la > yMin + 1e-4f || lb > yMin + 1e-4f) Slab(m, origin, u, facing, xa, xb, yMin, yMin, Mathf.Max(la, yMin), Mathf.Max(lb, yMin), uOff);
                // sopra la testa
                float ua2 = Mathf.Max(ha, yMin), ub2 = Mathf.Max(hb, yMin);
                if (ta > ua2 + 1e-4f || tb > ub2 + 1e-4f) Slab(m, origin, u, facing, xa, xb, ua2, ub2, ta, tb, uOff);
            }
        }

        // fetta di muro tra xa e xb: bordo basso (ya0 in xa, yb0 in xb), bordo alto (ya1, yb1)
        void Slab(Material m, Vector3 origin, Vector3 u, Vector3 facing, float xa, float xb, float ya0, float yb0, float ya1, float yb1, float uOff)
        {
            Vector3 A = origin + u * xa + Vector3.up * ya0, B = origin + u * xb + Vector3.up * yb0;
            Vector3 C = origin + u * xb + Vector3.up * yb1, D = origin + u * xa + Vector3.up * ya1;
            Quad(m, A, B, C, D, new Vector2(uOff + xa, ya0), new Vector2(uOff + xb, yb0), new Vector2(uOff + xb, yb1), new Vector2(uOff + xa, ya1), facing);
        }

        // ---------------------------------------------------------------- mesh finale
        public Mesh ToMesh(string name, out Material[] mats)
        {
            var mesh = new Mesh { name = name };
            int total = 0;
            foreach (var m in order) total += subs[m].v.Count;
            if (total > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var v = new List<Vector3>(total); var n = new List<Vector3>(total); var uv = new List<Vector2>(total);
            var tris = new List<int[]>();
            mats = order.ToArray();
            foreach (var m in order)
            {
                var s = subs[m];
                int off = v.Count;
                v.AddRange(s.v); n.AddRange(s.n); uv.AddRange(s.uv);
                var t = new int[s.t.Count];
                for (int i = 0; i < t.Length; i++) t[i] = s.t[i] + off;
                tris.Add(t);
            }
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = tris.Count;
            for (int i = 0; i < tris.Count; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }

    /// <summary>Profili di volte e archi in funzione della distanza dall'asse.</summary>
    public static class Arch
    {
        /// <summary>Altezza sopra l'imposta alla distanza x dall'asse: semiluce a, altezza dell'apice 'rise'.
        /// Acuto (due centri) se rise > a, altrimenti ellittico.</summary>
        public static float Y(float x, float a, float rise, bool pointed)
        {
            x = Mathf.Abs(x);
            if (x >= a) return 0f;
            if (pointed && rise > a)
            {
                float d = (rise * rise - a * a) / (2f * a);
                float R = a + d;
                float xx = x + d;
                return Mathf.Sqrt(Mathf.Max(0f, R * R - xx * xx));
            }
            float q = x / a;
            return rise * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q));
        }

        /// <summary>Normale (verso l'interno, cioe' verso chi guarda da sotto) della volta alla distanza x, nel piano (x, y).</summary>
        public static Vector2 InNormal(float x, float a, float rise, bool pointed)
        {
            float e = 0.01f;
            float y0 = Y(x - e, a, rise, pointed), y1 = Y(x + e, a, rise, pointed);
            var tg = new Vector2(2f * e, y1 - y0).normalized;
            return new Vector2(tg.y, -tg.x);   // ruotato: verso il basso e verso l'asse
        }
    }
}
