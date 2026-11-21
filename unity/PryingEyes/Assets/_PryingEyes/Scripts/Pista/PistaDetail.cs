using System.Threading.Tasks;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The two small tileable textures that the ground shader (PistaSurface) reads, made by code once per run, on a worker thread (it is pure arithmetic), so that the loading screen does not wait for them:
    ///  Grain: R = height (a few levels of lumps with a speckle of aggregate on top), G and B = the normal of that height (x and z, 0.5 = flat), A = a second smooth field for the patches of colour.
    ///  Stones: R = the dome of a Voronoi cell (0 on the border between two cells, 1 in the middle: a stone, or a crack when read at a larger scale), G and B = its normal, A = a random number per cell.
    /// Both tile (the noise and the cells wrap), have mipmaps and trilinear filtering with anisotropy, so a road seen at a grazing angle averages out instead of shimmering. They are data, not colours: linear.</summary>
    public static class PistaDetail
    {
        public const int Size = 512;
        public static Texture2D Grain { get; private set; }
        public static Texture2D Stones { get; private set; }
        public static bool Ready { get { return Grain != null && Stones != null; } }

        static Task<Color32[][]> job;

        /// <summary>Start making the textures in the background. Call it as early as possible; calling it again does nothing.</summary>
        public static void Begin()
        {
            if (job != null || Ready) return;
            job = Task.Run(Compute);
        }

        /// <summary>True when the pixels are ready (or the textures already exist, or the work failed: the world then keeps the plain materials).</summary>
        public static bool Done { get { return Ready || job == null || job.IsCompleted; } }

        /// <summary>Turn the finished pixels into textures. Must run on the main thread, after Done is true. A failed job leaves the textures empty.</summary>
        public static void Finish()
        {
            if (Ready || job == null || !job.IsCompleted) return;
            if (job.IsFaulted || job.IsCanceled) { Debug.LogWarning("[PryingEyes] pista: le texture del terreno non sono state fatte: " + (job.Exception != null ? job.Exception.GetBaseException().Message : "annullato")); job = null; return; }
            var px = job.Result;
            Grain = Make("PistaGrain", px[0]);
            Stones = Make("PistaStones", px[1]);
            job = null;
        }

        static Texture2D Make(string name, Color32[] pixels)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(pixels);
            t.Apply(true, false);
            return t;
        }

        static byte B(float v) { return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255); }

        static Color32[][] Compute()
        {
            return new[] { ComputeGrain(), ComputeStones() };
        }

        static Color32[] ComputeGrain()
        {
            int n = Size, mask = n - 1;
            var w = new float[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) w[y * n + x] = PistaTex.H2(x, y, 11);
            var h = new float[n * n];
            var field = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    int x1 = (x + 1) & mask, y1 = (y + 1) & mask;
                    float sp = 0.25f * (w[y * n + x] + w[y * n + x1] + w[y1 * n + x] + w[y1 * n + x1]);        // the 2x2 mean of white noise: grains a few texels across
                    sp = 0.5f + (sp - 0.5f) * 3.2f;                                                               // and the contrast the mean took away
                    float lump = PistaTex.Fbm(u, v, 4, 5, 101);
                    h[y * n + x] = Mathf.Clamp01(0.5f + (sp - 0.5f) * 0.8f + (lump - 0.5f) * 0.9f);
                    field[y * n + x] = PistaTex.Fbm(u, v, 9, 4, 202);
                }
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int xm = (x - 1) & mask, xp = (x + 1) & mask, ym = (y - 1) & mask, yp = (y + 1) & mask;
                    float nx = (h[y * n + xm] - h[y * n + xp]) * 1.7f, nz = (h[ym * n + x] - h[yp * n + x]) * 1.7f;
                    px[y * n + x] = new Color32(B(h[y * n + x]), B(nx * 0.5f + 0.5f), B(nz * 0.5f + 0.5f), B(field[y * n + x]));
                }
            return px;
        }

        static Color32[] ComputeStones()
        {
            const int cells = 20;
            int n = Size, mask = n - 1;
            var jx = new float[cells * cells];
            var jz = new float[cells * cells];
            var cid = new float[cells * cells];
            for (int cz = 0; cz < cells; cz++)
                for (int cx = 0; cx < cells; cx++)
                {
                    jx[cz * cells + cx] = 0.15f + 0.70f * PistaTex.H2(cx, cz, 31);
                    jz[cz * cells + cx] = 0.15f + 0.70f * PistaTex.H2(cx, cz, 47);
                    cid[cz * cells + cx] = PistaTex.H2(cx, cz, 77);
                }
            var dome = new float[n * n];
            var ids = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) / n * cells, fz = (y + 0.5f) / n * cells;
                    int cx0 = Mathf.FloorToInt(fx), cz0 = Mathf.FloorToInt(fz);
                    float d1 = 9f, d2 = 9f;
                    int best = 0;
                    for (int oz = -1; oz <= 1; oz++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int cx = cx0 + ox, cz = cz0 + oz;
                            int wx = ((cx % cells) + cells) % cells, wz = ((cz % cells) + cells) % cells;
                            int k = wz * cells + wx;
                            float dx = cx + jx[k] - fx, dz = cz + jz[k] - fz;
                            float dd = Mathf.Sqrt(dx * dx + dz * dz);
                            if (dd < d1) { d2 = d1; d1 = dd; best = k; } else if (dd < d2) d2 = dd;
                        }
                    float s = Mathf.Clamp01((d2 - d1) / 0.45f);
                    dome[y * n + x] = s * (2f - s);                                                              // round on top, steep at the border
                    ids[y * n + x] = cid[best];
                }
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int xm = (x - 1) & mask, xp = (x + 1) & mask, ym = (y - 1) & mask, yp = (y + 1) & mask;
                    float nx = (dome[y * n + xm] - dome[y * n + xp]) * 2.2f, nz = (dome[ym * n + x] - dome[yp * n + x]) * 2.2f;
                    px[y * n + x] = new Color32(B(dome[y * n + x]), B(nx * 0.5f + 0.5f), B(nz * 0.5f + 0.5f), B(ids[y * n + x]));
                }
            return px;
        }
    }
}
