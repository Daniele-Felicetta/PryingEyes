using System;
using System.Collections.Generic;

namespace PryingEyes.Proc.MapArt
{
    /// <summary>La penna: scrive linee, riempimenti e forme in una Mesh2D. Le linee sono nastri con i bordi sfumati (la texture di linea,
    /// vedi MapTextures.LineTex) e tremano come una mano vera: ogni punto disegnato e' spostato da un campo di tremore che dipende solo dalla
    /// sua posizione, quindi due tratti che si incontrano nello stesso punto restano uniti. Tutto e' in metri.</summary>
    public sealed class Pen
    {
        /// <summary>Quanto del nastro e' pieno (il resto e' sfumatura). Deve coincidere con la texture di linea.</summary>
        public const float Plateau = 0.56f;
        /// <summary>La coordinata di texture dei riempimenti pieni: il centro della texture di linea e' tutto opaco.</summary>
        public static readonly V2 Solid = new V2(0.5f, 0.5f);

        public readonly Mesh2D m;
        public int seed;
        /// <summary>0 = mano ferma, 1 = tremore normale, piu' di 1 = mano che trema.</summary>
        public float wobble = 1f;
        /// <summary>Ampiezza del tremore in metri a wobble = 1.</summary>
        public float tremor = 0.085f;
        /// <summary>Lunghezza dei pezzi in cui si spezza una linea lunga, in metri.</summary>
        public float step = 1.8f;

        public Pen(Mesh2D mesh, int seed) { m = mesh; this.seed = seed; }

        // ------------------------------------------------------------------ tremore
        public V2 Shake(V2 p, float k = 1f)
        {
            float a = tremor * wobble * k;
            if (a <= 0f) return p;
            return new V2(p.x + Noise.V1(p.x * 0.83f + p.y * 0.41f, seed) * a, p.y + Noise.V1(p.y * 0.87f - p.x * 0.37f, seed + 5) * a);
        }

        // ------------------------------------------------------------------ riempimenti (colore piatto)
        public void Fill(IList<V2> poly, Col c)
        {
            if (poly.Count < 3) return;
            V2 mid = default;
            foreach (var p in poly) mid += p;
            mid = mid * (1f / poly.Count);
            int hub = m.Vert(mid, Solid, c);
            int first = m.Count;
            foreach (var p in poly) m.Vert(p, Solid, c);
            for (int i = 0; i < poly.Count; i++) m.Tri(hub, first + i, first + (i + 1) % poly.Count);
        }

        /// <summary>Come Fill, ma con la coordinata di texture presa dalla posizione (per l'acquerello che si ripete): uv = metri x uvScale.</summary>
        public void FillTex(IList<V2> poly, Col c, float uvScale)
        {
            if (poly.Count < 3) return;
            V2 mid = default;
            foreach (var p in poly) mid += p;
            mid = mid * (1f / poly.Count);
            int hub = m.Vert(mid, mid * uvScale, c);
            int first = m.Count;
            foreach (var p in poly) m.Vert(p, p * uvScale, c);
            for (int i = 0; i < poly.Count; i++) m.Tri(hub, first + i, first + (i + 1) % poly.Count);
        }

        /// <summary>Come FillTex, ma il colore sfuma dal centro (hub) al bordo: l'inchiostro che si raccoglie dove nessuno ha guardato.</summary>
        public void FillTexGrad(IList<V2> poly, Col center, Col edge, float uvScale)
        {
            if (poly.Count < 3) return;
            V2 mid = default;
            foreach (var p in poly) mid += p;
            mid = mid * (1f / poly.Count);
            int hub = m.Vert(mid, mid * uvScale, center);
            int first = m.Count;
            foreach (var p in poly) m.Vert(p, p * uvScale, edge);
            for (int i = 0; i < poly.Count; i++) m.Tri(hub, first + i, first + (i + 1) % poly.Count);
        }

        /// <summary>Una macchia dell'atlante (cella 0..7 di MapTextures.StainAtlas) centrata in 'c', larga 'size' metri, ruotata di 'rot'.</summary>
        public void Stamp(int cell, V2 c, float size, float rot, Col col, float aspect = 1f)
        {
            MapTextures.StainUv(cell, out float u0, out float v0, out float u1, out float v1);
            V2 ux = V2.Polar(rot, size * 0.5f), uy = V2.Polar(rot + 1.5707963f, size * 0.5f * aspect);
            int i = m.Vert(c - ux - uy, new V2(u0, v0), col);
            m.Vert(c + ux - uy, new V2(u1, v0), col);
            m.Vert(c + ux + uy, new V2(u1, v1), col);
            m.Vert(c - ux + uy, new V2(u0, v1), col);
            m.Quad(i, i + 1, i + 2, i + 3);
        }

        public void Rect(R2 r, Col c)
        {
            int a = m.Vert(new V2(r.x0, r.y0), Solid, c), b = m.Vert(new V2(r.x0, r.y1), Solid, c), cc = m.Vert(new V2(r.x1, r.y1), Solid, c), d = m.Vert(new V2(r.x1, r.y0), Solid, c);
            m.Quad(a, b, cc, d);
        }

        /// <summary>Un quadrilatero qualunque (a, b, c, d in ordine).</summary>
        public void Quad4(V2 a, V2 b, V2 c, V2 d, Col col)
        {
            int i = m.Vert(a, Solid, col); m.Vert(b, Solid, col); m.Vert(c, Solid, col); m.Vert(d, Solid, col);
            m.Quad(i, i + 1, i + 2, i + 3);
        }

        public void Disc(V2 c, float r, Col col, int n = 20)
        {
            int hub = m.Vert(c, Solid, col);
            int first = m.Count;
            for (int i = 0; i < n; i++) m.Vert(c + V2.Polar(i * 6.2831853f / n, r), Solid, col);
            for (int i = 0; i < n; i++) m.Tri(hub, first + i, first + (i + 1) % n);
        }

        /// <summary>Un cerchio con il centro piu' trasparente verso il bordo: una macchia morbida (luce di lampada, alone).</summary>
        public void Glow(V2 c, float r, Col inner, int n = 24)
        {
            int hub = m.Vert(c, Solid, inner);
            var edge = inner.A(0f);
            int first = m.Count;
            for (int i = 0; i < n; i++) m.Vert(c + V2.Polar(i * 6.2831853f / n, r), Solid, edge);
            for (int i = 0; i < n; i++) m.Tri(hub, first + i, first + (i + 1) % n);
        }

        /// <summary>Una macchia irregolare (inchiostro, sangue, nebbia): il raggio cambia con l'angolo.</summary>
        public void Blob(V2 c, float rx, float ry, Col col, int blobSeed, float rough = 0.28f, int n = 28)
        {
            int hub = m.Vert(c, Solid, col);
            int first = m.Count;
            for (int i = 0; i < n; i++)
            {
                float a = i * 6.2831853f / n;
                float k = 1f + rough * (Noise.V1(i * 0.9f, blobSeed) * 0.6f + Noise.V1(i * 2.3f + 11f, blobSeed + 3) * 0.4f);
                m.Vert(c + new V2((float)Math.Cos(a) * rx * k, (float)Math.Sin(a) * ry * k), Solid, col);
            }
            for (int i = 0; i < n; i++) m.Tri(hub, first + i, first + (i + 1) % n);
        }

        // ------------------------------------------------------------------ linee
        /// <summary>Un nastro dritto con i bordi sfumati; 'w' e' la larghezza della parte piena. 'cap' allunga le estremita' di mezza larghezza.</summary>
        public void Line(V2 a, V2 b, float w, Col c, bool cap = true)
        {
            V2 d = b - a;
            float len = d.Len;
            if (len < 1e-5f && !cap) return;
            d = len > 1e-5f ? d * (1f / len) : new V2(1f, 0f);
            V2 n = d.Perp * (w / Plateau * 0.5f);
            V2 e = d * (cap ? w * 0.5f : 0f);
            V2 a0 = a - e, b0 = b + e;
            int i = m.Vert(a0 - n, new V2(0.5f, 0f), c);
            m.Vert(a0 + n, new V2(0.5f, 1f), c);
            m.Vert(b0 + n, new V2(0.5f, 1f), c);
            m.Vert(b0 - n, new V2(0.5f, 0f), c);
            m.Quad(i, i + 1, i + 2, i + 3);
        }

        /// <summary>Una linea a mano: spezzata in pezzi, con il tremore e con lo spessore che cambia un poco.</summary>
        public void Ink(V2 a, V2 b, float w, Col c, float amp = 1f)
        {
            float len = (b - a).Len;
            int n = Math.Max(1, (int)Math.Ceiling(len / step));
            V2 prev = Shake(a, amp);
            for (int i = 1; i <= n; i++)
            {
                V2 q = Shake(V2.Lerp(a, b, i / (float)n), amp);
                float k = 1f + 0.09f * Noise.V1(q.x * 1.1f + q.y * 1.3f, seed + 9);
                Line(prev, q, w * k, c);
                prev = q;
            }
        }

        public void Path(IList<V2> pts, bool closed, float w, Col c, float amp = 1f)
        {
            int n = pts.Count;
            for (int i = 0; i + 1 < n; i++) Ink(pts[i], pts[i + 1], w, c, amp);
            if (closed && n > 2) Ink(pts[n - 1], pts[0], w, c, amp);
        }

        public void Arc(V2 c, float r, float a0, float a1, float w, Col col, int n = 0, float amp = 1f)
        {
            if (n <= 0) n = Math.Max(4, (int)(Math.Abs(a1 - a0) * Math.Max(r, 0.4f) * 1.6f));
            V2 prev = Shake(c + V2.Polar(a0, r), amp);
            for (int i = 1; i <= n; i++)
            {
                V2 q = Shake(c + V2.Polar(a0 + (a1 - a0) * i / n, r), amp);
                Line(prev, q, w, col);
                prev = q;
            }
        }

        public void Ring(V2 c, float r, float w, Col col, int n = 0, float amp = 1f) => Arc(c, r, 0f, 6.2831853f, w, col, n, amp);

        /// <summary>Linea tratteggiata (trattini lunghi 'dash', vuoti lunghi 'gap').</summary>
        public void Dashed(V2 a, V2 b, float w, Col c, float dash, float gap, bool tremble = false)
        {
            V2 d = b - a;
            float len = d.Len;
            if (len < 1e-4f) return;
            V2 u = d * (1f / len);
            for (float s = 0f; s < len; s += dash + gap)
            {
                float e = Math.Min(len, s + dash);
                if (tremble) Ink(a + u * s, a + u * e, w, c);
                else Line(a + u * s, a + u * e, w, c, false);
            }
        }

        /// <summary>Tratteggio (linee parallele a 'angle' radianti) dentro un rettangolo; 'seedOffset' sfasa le righe e ne accorcia qualcuna.</summary>
        public void Hatch(R2 r, float angle, float spacing, float w, Col c, int hatchSeed = 0, float drop = 0f)
        {
            V2 dir = V2.Polar(angle), nrm = dir.Perp;
            V2 mid = r.Center;
            float reach = (float)Math.Sqrt(r.W * r.W + r.H * r.H) * 0.5f;
            int count = (int)(reach / spacing);
            for (int i = -count; i <= count; i++)
            {
                if (drop > 0f && Noise.R(i, hatchSeed) < drop) continue;
                V2 o = mid + nrm * (i * spacing);
                // interseca la retta con il rettangolo (clip di Liang-Barsky)
                float t0 = -reach * 2f, t1 = reach * 2f;
                if (!Clip(o.x, dir.x, r.x0, r.x1, ref t0, ref t1) || !Clip(o.y, dir.y, r.y0, r.y1, ref t0, ref t1)) continue;
                if (t1 - t0 < spacing * 0.5f) continue;
                float j0 = (Noise.R(i, hatchSeed + 1) - 0.5f) * spacing * 0.8f, j1 = (Noise.R(i, hatchSeed + 2) - 0.5f) * spacing * 0.8f;
                Line(o + dir * (t0 + Math.Max(0f, j0)), o + dir * (t1 - Math.Max(0f, j1)), w, c, false);
            }
        }

        static bool Clip(float o, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (Math.Abs(d) < 1e-6f) return o >= lo && o <= hi;
            float a = (lo - o) / d, b = (hi - o) / d;
            if (a > b) { float t = a; a = b; b = t; }
            if (a > t0) t0 = a;
            if (b < t1) t1 = b;
            return t1 > t0;
        }

        // ------------------------------------------------------------------ forme comuni
        /// <summary>Un rettangolo orientato (centro, larghezza lungo 'yaw', profondita'), come l'impronta di un arredo. 'yaw' in radianti dall'asse x.</summary>
        public static V2[] OrientedRect(V2 center, float w, float d, float yaw)
        {
            V2 ux = V2.Polar(yaw, w * 0.5f), uy = V2.Polar(yaw + 1.5707963f, d * 0.5f);
            return new[] { center - ux - uy, center + ux - uy, center + ux + uy, center - ux + uy };
        }

        /// <summary>Il contorno di un rettangolo con gli angoli arrotondati (raggio r), in senso antiorario, 'seg' segmenti per angolo.</summary>
        public static List<V2> RoundedRect(R2 r, float radius, int seg = 6)
        {
            var pts = new List<V2>();
            radius = Math.Min(radius, Math.Min(r.W, r.H) * 0.5f);
            if (radius < 0.05f)
            {
                pts.Add(new V2(r.x0, r.y0)); pts.Add(new V2(r.x1, r.y0)); pts.Add(new V2(r.x1, r.y1)); pts.Add(new V2(r.x0, r.y1));
                return pts;
            }
            void Corner(float cx, float cy, float a0)
            {
                for (int i = 0; i <= seg; i++) pts.Add(new V2(cx, cy) + V2.Polar(a0 + 1.5707963f * i / seg, radius));
            }
            Corner(r.x1 - radius, r.y0 + radius, -1.5707963f);
            Corner(r.x1 - radius, r.y1 - radius, 0f);
            Corner(r.x0 + radius, r.y1 - radius, 1.5707963f);
            Corner(r.x0 + radius, r.y0 + radius, 3.1415927f);
            return pts;
        }

        public void Poly(IList<V2> pts, bool closed, float w, Col c) => Path(pts, closed, w, c, 1f);

        public void Cross(V2 c, float r, float w, Col col)
        {
            Ink(c + new V2(-r, -r), c + new V2(r, r), w, col);
            Ink(c + new V2(-r, r), c + new V2(r, -r), w, col);
        }
    }
}
