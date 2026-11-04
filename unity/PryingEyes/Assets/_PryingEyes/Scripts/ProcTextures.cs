using UnityEngine;

namespace PryingEyes
{
    /// <summary>Procedural Texture2D generators (no downloads): marble checker, wallpaper stripes, plaster.</summary>
    public static class ProcTextures
    {
        public static Texture2D Checker(int size = 512, int cells = 8, Color? a = null, Color? b = null)
        {
            var cA = a ?? new Color(0.92f, 0.90f, 0.86f);
            var cB = b ?? new Color(0.05f, 0.05f, 0.07f);
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true);
            t.filterMode = FilterMode.Trilinear;
            t.wrapMode = TextureWrapMode.Repeat;
            int cell = size / cells;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool even = ((x / cell) + (y / cell)) % 2 == 0;
                    Color c = even ? cA : cB;
                    float v = 0.97f + 0.03f * Mathf.Sin(x * 0.11f) * Mathf.Sin(y * 0.13f);
                    px[y * size + x] = c * v;
                }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        public static Texture2D Stripes(int size = 512, int stripes = 24, Color? a = null, Color? b = null)
        {
            var cA = a ?? new Color(0.55f, 0.38f, 0.28f);
            var cB = b ?? new Color(0.42f, 0.28f, 0.20f);
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true);
            t.filterMode = FilterMode.Trilinear;
            t.wrapMode = TextureWrapMode.Repeat;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int s = (x * stripes) / size;
                    Color c = (s % 2 == 0) ? cA : cB;
                    float n = 0.96f + 0.04f * Mathf.Sin(y * 0.35f + s * 1.7f);
                    px[y * size + x] = c * n;
                }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        public static Texture2D Plaster(int size = 256, Color? baseC = null)
        {
            var bc = baseC ?? new Color(0.78f, 0.74f, 0.68f);
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true);
            t.wrapMode = TextureWrapMode.Repeat;
            var px = new Color[size * size];
            var rnd = new System.Random(1234);
            for (int i = 0; i < px.Length; i++)
            {
                float n = 0.94f + 0.06f * (float)rnd.NextDouble();
                px[i] = bc * n;
            }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }
    }
}
