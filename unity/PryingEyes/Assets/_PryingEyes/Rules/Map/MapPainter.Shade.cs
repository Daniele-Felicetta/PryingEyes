using System;
using System.Collections.Generic;

namespace PryingEyes.Proc.MapArt
{
    public sealed partial class MapPainter
    {
        const float SootDepth = 4.6f, SootAlpha = 0.46f, InnerDepth = 1.7f, InnerAlpha = 0.24f;

        /// <summary>L'ombra dei muri. Dentro, una sfumatura debole lungo tutto il perimetro che da' spessore alla stanza; fuori, sotto ogni muro che
        /// dà sul buio (il vuoto o una stanza che nessuno ha visto), il nero che si infila e sporca la carta: la parte esplorata della mappa
        /// sembra un'isola circondata da qualcosa che avanza.</summary>
        void PaintShade(Room r, Art art)
        {
            var rect = RectOf(r);
            float rad = r.cornerRadius;
            var wash = art.wash;
            var loop = Resample(Pen.RoundedRect(rect, rad, 6), true, 1.1f);
            Ribbon(wash, loop, true, -1f, InnerDepth, InnerAlpha, MapStyle.Smudge, false, false, r.seed + 41);

            foreach (var side in Sides)
            {
                bool alongX = Plan.AlongX(side);
                float line = Plan.Line(r, side) * 0.1f;
                Plan.Span(r, side, out int lo, out int hi);
                bool forward = side == Side.S || side == Side.E;      // l'ordine antiorario della stanza
                foreach (var seg in plan.Segments(r, side))
                {
                    if (seg.other >= 0 && s.seen[seg.other] == RoomSeen.Seen) continue;
                    float a = seg.a0 * 0.1f, b = seg.a1 * 0.1f;
                    bool atLo = seg.a0 == lo, atHi = seg.a1 == hi;
                    if (rad > 0f) { if (atLo) a += rad; if (atHi) b -= rad; }
                    else { if (atLo) a -= 1.6f; if (atHi) b += 1.6f; }  // oltre l'angolo, dove prende il posto la striscia del lato accanto
                    if (b - a < 0.6f) continue;
                    int k = Math.Max(1, (int)Math.Ceiling((b - a) / 1.1f));
                    var pts = new List<V2>(k + 1);
                    for (int j = 0; j <= k; j++)
                    {
                        float t = forward ? a + (b - a) * j / k : b - (b - a) * j / k;
                        pts.Add(alongX ? new V2(t, line) : new V2(line, t));
                    }
                    Ribbon(wash, pts, false, 1f, SootDepth, SootAlpha, MapStyle.Smudge, true, true, r.seed + 51 + (int)side);
                }
            }
            if (rad > 0f)
            {
                SootCorner(wash, new V2(rect.x1 - rad, rect.y0 + rad), rad, -1.5707963f, r.seed + 61);
                SootCorner(wash, new V2(rect.x1 - rad, rect.y1 - rad), rad, 0f, r.seed + 62);
                SootCorner(wash, new V2(rect.x0 + rad, rect.y1 - rad), rad, 1.5707963f, r.seed + 63);
                SootCorner(wash, new V2(rect.x0 + rad, rect.y0 + rad), rad, 3.1415927f, r.seed + 64);
            }
        }

        static void SootCorner(Mesh2D mesh, V2 c, float rad, float a0, int seed)
        {
            int n = Math.Max(4, (int)(rad * 1.5707963f / 1.1f));
            var pts = new List<V2>(n + 1);
            for (int i = 0; i <= n; i++) pts.Add(c + V2.Polar(a0 + 1.5707963f * i / n, rad));
            Ribbon(mesh, pts, false, 1f, SootDepth, SootAlpha, MapStyle.Smudge, false, false, seed);
        }

        /// <summary>Una striscia sfumata lungo una poligonale antioraria: dal bordo (alfa pieno) fino a una profondita' che cambia con il rumore,
        /// dove sparisce. sign = +1 verso l'esterno (a destra del senso di marcia), -1 verso l'interno.</summary>
        static void Ribbon(Mesh2D mesh, IList<V2> pts, bool closed, float sign, float depth, float alpha, Col col, bool taperStart, bool taperEnd, int seed)
        {
            int n = pts.Count;
            if (n < 2) return;
            int first = mesh.Count;
            for (int i = 0; i < n; i++)
            {
                V2 p = pts[i];
                V2 prev = pts[closed ? (i + n - 1) % n : Math.Max(0, i - 1)], next = pts[closed ? (i + 1) % n : Math.Min(n - 1, i + 1)];
                V2 d = (next - prev).Norm;
                V2 nrm = new V2(d.y, -d.x) * sign;
                float f = Noise.Fbm(p.x * 0.20f, p.y * 0.20f, seed, 3);
                float g = Noise.Fbm(p.x * 0.55f + 17f, p.y * 0.55f - 5f, seed + 7, 2);
                float dep = depth * (0.30f + 1.40f * f);
                float a = alpha * (0.30f + 1.40f * g);
                if (!closed)
                {
                    if (taperStart) a *= Math.Min(1f, i / 2f);
                    if (taperEnd) a *= Math.Min(1f, (n - 1 - i) / 2f);
                }
                V2 q = p + nrm * dep;
                mesh.Vert(p, p * WashUv, col.A(Math.Min(a, 0.92f)));
                mesh.Vert(q, q * WashUv, col.A(0f));
            }
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                int a0 = first + i * 2, b0 = first + ((i + 1) % n) * 2;
                mesh.Quad(a0, a0 + 1, b0 + 1, b0);
            }
        }

        /// <summary>I punti di una poligonale ogni 'step' metri al massimo.</summary>
        static List<V2> Resample(IList<V2> pts, bool closed, float step)
        {
            var o = new List<V2>();
            int n = pts.Count;
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                V2 a = pts[i], b = pts[(i + 1) % n];
                int k = Math.Max(1, (int)Math.Ceiling((b - a).Len / step));
                for (int j = 0; j < k; j++) o.Add(V2.Lerp(a, b, j / (float)k));
            }
            if (!closed) o.Add(pts[n - 1]);
            return o;
        }
    }
}
