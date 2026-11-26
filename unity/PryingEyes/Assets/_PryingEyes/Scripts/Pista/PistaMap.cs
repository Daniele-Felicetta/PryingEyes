using UnityEngine;
using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>Draws the circuit as a picture: a dark outline with a pale core along the centre line, a red bar at the start line. The race map in the corner of the screen and the preview in the menu
    /// both use it; the caller gets the scale and offset that put a point of the world onto the picture (pixels = world * k + offset).</summary>
    public static class PistaMap
    {
        public struct Fit { public float k, ox, oz; public int size; }

        /// <summary>The scale and the offsets that fit a circuit in a square of 'size' pixels with 'pad' pixels of margin.</summary>
        public static Fit FitFor(Circuit c, int size, float pad)
        {
            float w = c.maxX - c.minX, h = c.maxZ - c.minZ;
            var f = new Fit { size = size };
            f.k = (size - 2f * pad) / Mathf.Max(w, h, 1f);
            f.ox = pad + ((size - 2f * pad) - w * f.k) * 0.5f - c.minX * f.k;
            f.oz = pad + ((size - 2f * pad) - h * f.k) * 0.5f - c.minZ * f.k;
            return f;
        }

        /// <summary>The picture of a circuit (transparent outside the track). 'lineWidth' is the width of the pale core in pixels.</summary>
        public static Texture2D Paint(Circuit c, Fit f, float lineWidth, string name)
        {
            int N = f.size;
            var outline = new float[N * N];
            var core = new float[N * N];
            int step = 3, segs = c.n / step;
            for (int s = 0; s < segs; s++)
            {
                int i0 = (s * step) % c.n, i1 = ((s + 1) * step) % c.n;
                float x0 = c.px[i0] * f.k + f.ox, y0 = c.pz[i0] * f.k + f.oz, x1 = c.px[i1] * f.k + f.ox, y1 = c.pz[i1] * f.k + f.oz;
                Line(outline, N, x0, y0, x1, y1, lineWidth * 2f);
                Line(core, N, x0, y0, x1, y1, lineWidth);
            }
            float sx = c.px[0] * f.k + f.ox, sz = c.pz[0] * f.k + f.oz;
            Line(outline, N, sx - c.nx[0] * 9f, sz - c.nz[0] * 9f, sx + c.nx[0] * 9f, sz + c.nz[0] * 9f, 3.2f);
            var bar = new float[N * N];
            Line(bar, N, sx - c.nx[0] * 8f, sz - c.nz[0] * 8f, sx + c.nx[0] * 8f, sz + c.nz[0] * 8f, 1.6f);
            var px = new Color32[N * N];
            for (int k = 0; k < px.Length; k++)
            {
                float a = Mathf.Clamp01(outline[k]), b = Mathf.Clamp01(core[k]);
                float rr = Mathf.Lerp(0.02f, 0.92f, b), gg = Mathf.Lerp(0.02f, 0.93f, b), bb = Mathf.Lerp(0.03f, 0.90f, b);
                float alpha = Mathf.Max(a * 0.85f, b);
                float t = Mathf.Clamp01(bar[k]);
                if (t > 0.01f) { rr = Mathf.Lerp(rr, 1f, t); gg = Mathf.Lerp(gg, 0.31f, t); bb = Mathf.Lerp(bb, 0.24f, t); alpha = Mathf.Max(alpha, t); }
                px[k] = new Color32((byte)(rr * 255f), (byte)(gg * 255f), (byte)(bb * 255f), (byte)(alpha * 255f));
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); tex.Apply(false, true);
            return tex;
        }

        /// <summary>A thick line with soft edges into a coverage buffer (the largest coverage of every pixel it touches wins).</summary>
        static void Line(float[] buf, int N, float x0, float y0, float x1, float y1, float width)
        {
            float r = width * 0.5f;
            int minx = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x0, x1) - r - 1f)), maxx = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(x0, x1) + r + 1f));
            int miny = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y0, y1) - r - 1f)), maxy = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(y0, y1) + r + 1f));
            float dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
            for (int y = miny; y <= maxy; y++)
                for (int x = minx; x <= maxx; x++)
                {
                    float px = x + 0.5f - x0, py = y + 0.5f - y0;
                    float t = len2 > 1e-6f ? Mathf.Clamp01((px * dx + py * dy) / len2) : 0f;
                    float ex = px - dx * t, ey = py - dy * t;
                    float d = Mathf.Sqrt(ex * ex + ey * ey);
                    float cv = Mathf.Clamp01(r - d + 0.5f);
                    int k = y * N + x;
                    if (cv > buf[k]) buf[k] = cv;
                }
        }
    }
}
