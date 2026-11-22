using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The two cards that every tree of a circuit is made of, drawn by code once per run on a worker thread (pure arithmetic): a cluster of broadleaf leaves (a blob of a hundred and fifty small pointed
    /// leaves with a vein, holes of sky between them) and a fir frond (a stem with branchlets, and needles along them). RGB is the colour, A the coverage; the shader cuts at one half.
    /// An alpha-tested texture thins out in its mipmaps (the half-covered texels average to less than half), so a tree seen from far would lose its leaves: the mips are made by hand, each level's alpha scaled
    /// so that the same share of it stands above the cutoff as in the top level, and the colour is averaged weighted by alpha so that no dark fringe creeps in.</summary>
    public static class PistaFoliage
    {
        /// <summary>The side of the leaf card (many small leaves need the room) and of the frond card, in texels.</summary>
        public const int LeafSize = 512, NeedleSize = 256;
        public static Texture2D Leaf { get; private set; }
        public static Texture2D Needle { get; private set; }
        public static bool Ready { get { return Leaf != null && Needle != null; } }

        static Task<Color32[][][]> job;

        /// <summary>Start drawing the cards in the background (calling it again does nothing).</summary>
        public static void Begin()
        {
            if (job != null || Ready) return;
            job = Task.Run(Compute);
        }

        public static bool Done { get { return Ready || job == null || job.IsCompleted; } }

        /// <summary>Turn the finished mip chains into textures (main thread, after Done). A failed job leaves them empty: the trees then use a plain material.</summary>
        public static void Finish()
        {
            if (Ready || job == null || !job.IsCompleted) return;
            if (job.IsFaulted || job.IsCanceled) { Debug.LogWarning("[PryingEyes] pista: le carte degli alberi non sono state fatte: " + (job.Exception != null ? job.Exception.GetBaseException().Message : "annullato")); job = null; return; }
            var r = job.Result;
            Leaf = Make("PistaLeaf", LeafSize, r[0]);
            Needle = Make("PistaNeedle", NeedleSize, r[1]);
            job = null;
        }

        static Texture2D Make(string name, int size, Color32[][] mips)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            for (int level = 0; level < mips.Length; level++) t.SetPixels32(mips[level], level);
            t.Apply(false, false);
            return t;
        }

        static Color32[][][] Compute() { return new[] { Mips(DrawLeafCluster(), LeafSize, LeafSize), Mips(DrawFrond(), NeedleSize, NeedleSize) }; }

        // ------------------------------------------------------------------ the mip chain that keeps its coverage
        static float Coverage(Color32[] px)
        {
            int n = 0;
            for (int i = 0; i < px.Length; i++) if (px[i].a >= 128) n++;
            return n / (float)px.Length;
        }

        internal static Color32[][] Mips(Color32[] top, int width, int height)
        {
            var list = new List<Color32[]> { top };
            float target = Coverage(top);
            var cur = top;
            int cw = width, ch = height;
            while (cw > 1 || ch > 1)
            {
                int nw = Mathf.Max(1, cw / 2), nh = Mathf.Max(1, ch / 2);
                var next = new Color32[nw * nh];
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                    {
                        float sa = 0f, sr = 0f, sg = 0f, sb = 0f;
                        for (int k = 0; k < 4; k++)
                        {
                            var p = cur[Mathf.Min(y * 2 + (k >> 1), ch - 1) * cw + Mathf.Min(x * 2 + (k & 1), cw - 1)];
                            float a = p.a / 255f;
                            sa += a; sr += p.r * a; sg += p.g * a; sb += p.b * a;
                        }
                        float w = sa > 1e-4f ? 1f / sa : 0f;
                        next[y * nw + x] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(sr * w), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(sg * w), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(sb * w), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(sa * 0.25f * 255f), 0, 255));
                    }
                // the smallest gain that puts the same share above the cutoff as the top level has
                float lo = 1f, hi = 8f;
                for (int it = 0; it < 14; it++)
                {
                    float mid = 0.5f * (lo + hi);
                    int above = 0;
                    for (int i = 0; i < next.Length; i++) if (next[i].a * mid >= 128f) above++;
                    if (above / (float)next.Length >= target) hi = mid; else lo = mid;
                }
                for (int i = 0; i < next.Length; i++) { var p = next[i]; p.a = (byte)Mathf.Clamp(Mathf.RoundToInt(p.a * hi), 0, 255); next[i] = p; }
                list.Add(next); cur = next; cw = nw; ch = nh;
            }
            return list.ToArray();
        }

        // ------------------------------------------------------------------ drawing
        sealed class Canvas
        {
            public readonly int n;
            public readonly float[] a, r, g, b;
            public Canvas(int n) { this.n = n; a = new float[n * n]; r = new float[n * n]; g = new float[n * n]; b = new float[n * n]; }

            /// <summary>A pointed ellipse centred at (cx, cy), half length 'len' along the direction 'rot' (radians), half width 'wid' at the middle, with a lighter vein and a darker edge.</summary>
            public void Leaf(float cx, float cy, float len, float wid, float rot, float cr, float cg, float cb, float vein)
            {
                float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                int reach = Mathf.CeilToInt(len) + 2;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx) - reach), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(cx) + reach);
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy) - reach), y1 = Mathf.Min(n - 1, Mathf.CeilToInt(cy) + reach);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float u = dx * cs + dy * sn, v = -dx * sn + dy * cs;
                        float t = u / len;
                        if (t <= -1f || t >= 1f) continue;
                        float w = wid * Mathf.Pow(1f - t * t, 0.8f);
                        float av = Mathf.Abs(v);
                        if (av >= w + 0.5f) continue;
                        float cov = Mathf.Clamp01(w - av + 0.5f);
                        float edge = w > 1e-3f ? Mathf.Clamp01(av / w) : 1f;
                        float shade = 1f - 0.28f * edge * edge + (av < 0.9f ? vein : 0f);
                        int i = y * n + x;
                        if (cov >= a[i] * 0.6f) { r[i] = Mathf.Lerp(r[i], cr * shade, cov); g[i] = Mathf.Lerp(g[i], cg * shade, cov); b[i] = Mathf.Lerp(b[i], cb * shade, cov); }
                        if (cov > a[i]) a[i] = cov;
                    }
            }

            public Color32[] ToPixels()
            {
                var px = new Color32[n * n];
                for (int i = 0; i < px.Length; i++)
                    px[i] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(r[i] * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(g[i] * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(b[i] * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(a[i] * 255f), 0, 255));
                return px;
            }
        }

        static float Rf(System.Random rng, float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

        /// <summary>A blob of broadleaf leaves: they are laid in a disc, thinner toward the rim, each with its own size, turn and tone (lighter in the upper half of the card, where the sun reaches).</summary>
        static Color32[] DrawLeafCluster()
        {
            int n = LeafSize;
            var cv = new Canvas(n);
            var rng = new System.Random(4711);
            float centre = n * 0.5f;
            for (int k = 0; k < 640; k++)
            {
                float ang = Rf(rng, 0f, Mathf.PI * 2f), rad = Mathf.Sqrt(Rf(rng, 0f, 1f)) * n * 0.43f;
                // thinner toward the rim: some leaves of the outer ring are not drawn
                if (rad > n * 0.30f && Rf(rng, 0f, 1f) < (rad / (n * 0.43f) - 0.65f) * 1.6f) continue;
                float cx = centre + Mathf.Cos(ang) * rad, cy = centre + Mathf.Sin(ang) * rad;
                float len = Rf(rng, 0.034f, 0.056f) * n, wid = len * Rf(rng, 0.42f, 0.58f);
                float up = Mathf.Clamp01(1f - cy / n);                                    // the top of the card is the top of the picture
                float tone = Rf(rng, 0.78f, 1.12f) * (0.88f + 0.22f * up);
                cv.Leaf(cx, cy, len, wid, Rf(rng, 0f, Mathf.PI * 2f), 0.36f * tone, 0.50f * tone, 0.20f * tone, 0.06f);
            }
            return cv.ToPixels();
        }

        /// <summary>A fir frond with its stem up the middle (the base at the bottom of the picture): branchlets going up and out on both sides, longest at the bottom, and needles along each.</summary>
        static Color32[] DrawFrond()
        {
            int n = NeedleSize;
            var cv = new Canvas(n);
            var rng = new System.Random(918);
            float cx = n * 0.5f;
            // the stem
            cv.Leaf(cx, n * 0.5f, n * 0.5f - 2f, 2.2f, Mathf.PI * 0.5f, 0.35f, 0.30f, 0.20f, 0f);
            const int branchlets = 40;
            for (int k = 0; k < branchlets; k++)
            {
                float t = k / (float)(branchlets - 1);                                    // 0 at the base, 1 at the tip
                float y = n - 8f - t * (n - 22f);
                float len = n * 0.46f * Mathf.Pow(1f - t, 0.80f) + 7f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float spread = Mathf.Deg2Rad * Rf(rng, 48f, 64f);                    // from the stem, up and out
                    float dirx = side * Mathf.Sin(spread), diry = -Mathf.Cos(spread);
                    float l = len * Rf(rng, 0.85f, 1.1f);
                    float tone = Rf(rng, 0.85f, 1.1f);
                    // the branchlet: a thin leaf from the stem outward
                    cv.Leaf(cx + dirx * l * 0.5f, y + diry * l * 0.5f, l * 0.5f, 1.9f, Mathf.Atan2(diry, dirx), 0.19f * tone, 0.29f * tone, 0.15f * tone, 0f);
                    // and the needles along it, both sides, leaning toward the tip
                    int needles = Mathf.Max(3, Mathf.RoundToInt(l / 5.5f));
                    for (int m = 1; m <= needles; m++)
                    {
                        float s = m / (float)(needles + 1) * l;
                        float px = cx + dirx * s, py = y + diry * s;
                        float shorten = 1f - 0.45f * (s / l);
                        for (int ns = -1; ns <= 1; ns += 2)
                        {
                            float na = Mathf.Atan2(diry, dirx) + ns * Mathf.Deg2Rad * Rf(rng, 38f, 58f);
                            float nl = Rf(rng, 7f, 11f) * shorten;
                            cv.Leaf(px + Mathf.Cos(na) * nl * 0.5f, py + Mathf.Sin(na) * nl * 0.5f, nl * 0.5f, 1.35f, na, 0.23f * tone * (0.8f + 0.4f * (s / l)), 0.36f * tone * (0.8f + 0.4f * (s / l)), 0.19f * tone, 0f);
                        }
                    }
                }
            }
            return cv.ToPixels();
        }
    }
}
