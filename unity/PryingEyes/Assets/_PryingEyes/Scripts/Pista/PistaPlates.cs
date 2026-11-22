using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The race numbers on the plates of the bikes: a small picture for every number (a white plate with a border in the colour of the livery and the number in black, a five by seven letter table
    /// drawn large and then rounded off by a blur and a soft threshold so that the corners are not squares), made on demand on the main thread (128 by 80 texels: nothing to wait for) and kept for the rest of
    /// the run, shared by every bike. A bike with the number 0 has a blank plate.</summary>
    public static class PistaPlates
    {
        public const int W = 128, H = 80;
        static readonly Dictionary<long, Texture2D> cache = new Dictionary<long, Texture2D>();

        // the digits 0 to 9, seven rows of five bits (the top row first, the leftmost column the highest bit)
        static readonly byte[][] Digit =
        {
            new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E }, new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E }, new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F },
            new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E }, new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 }, new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
            new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E }, new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 }, new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E },
            new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
        };

        /// <summary>The picture of a plate with this number and this colour of border (cached).</summary>
        public static Texture2D Picture(int number, Color border)
        {
            Color32 b = border;
            long key = ((long)number << 32) | ((long)b.r << 16) | ((long)b.g << 8) | b.b;
            Texture2D t;
            if (cache.TryGetValue(key, out t) && t != null) return t;
            t = Draw(number, b);
            cache[key] = t;
            return t;
        }

        /// <summary>How many texels of a plate the number covers, from the same mask the picture is made from (for the tests: with no graphics device a texture has no pixels to read back).</summary>
        public static int InkTexels(int number)
        {
            int n = 0;
            foreach (float f in Mask(number)) if (f > 0.5f) n++;
            return n;
        }

        /// <summary>The number of a plate as ink, 0 to 1 over 128 by 80 texels: the five by seven digits, then rounded by two blurs.</summary>
        static float[] Mask(int number)
        {
            var mask = new float[W * H];
            string s = number > 0 ? number.ToString() : "";
            if (s.Length > 0)
            {
                int scale = s.Length >= 3 ? 6 : 9;                                   // texels to a dot of the table (two digits fill the plate: 11 dots by 7 on 128 by 80)
                int width = s.Length * 6 * scale - scale, x0 = (W - width) / 2, y0 = (H - 7 * scale) / 2;
                for (int i = 0; i < s.Length; i++)
                {
                    var rows = Digit[s[i] - '0'];
                    for (int r = 0; r < 7; r++)
                        for (int c = 0; c < 5; c++)
                            if ((rows[r] & (1 << (4 - c))) != 0)
                                for (int yy = 0; yy < scale; yy++)
                                    for (int xx = 0; xx < scale; xx++)
                                    {
                                        int x = x0 + i * 6 * scale + c * scale + xx, y = y0 + (6 - r) * scale + yy;
                                        if (x >= 0 && y >= 0 && x < W && y < H) mask[y * W + x] = 1f;
                                    }
                }
                mask = Blur(mask, 2);                                                // a box blur twice over: round corners
                mask = Blur(mask, 2);
            }
            return mask;
        }

        static Texture2D Draw(int number, Color32 border)
        {
            var mask = Mask(number);
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int edge = Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y));
                    Color32 bg = edge < 5 ? border : new Color32(238, 238, 232, 255);
                    float k = Mathf.SmoothStep(0.30f, 0.62f, mask[y * W + x]);
                    px[y * W + x] = new Color32((byte)Mathf.RoundToInt(Mathf.Lerp(bg.r, 18f, k)), (byte)Mathf.RoundToInt(Mathf.Lerp(bg.g, 18f, k)), (byte)Mathf.RoundToInt(Mathf.Lerp(bg.b, 20f, k)), 255);
                }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true, false) { name = "PistaTarga" + number, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        static float[] Blur(float[] a, int r)
        {
            var tmp = new float[a.Length]; var o = new float[a.Length];
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float sum = 0f;
                    for (int d = -r; d <= r; d++) sum += a[y * W + Mathf.Clamp(x + d, 0, W - 1)];
                    tmp[y * W + x] = sum * inv;
                }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float sum = 0f;
                    for (int d = -r; d <= r; d++) sum += tmp[Mathf.Clamp(y + d, 0, H - 1) * W + x];
                    o[y * W + x] = sum * inv;
                }
            return o;
        }
    }
}
