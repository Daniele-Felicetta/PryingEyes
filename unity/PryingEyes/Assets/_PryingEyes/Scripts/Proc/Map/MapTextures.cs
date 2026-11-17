using System;

namespace PryingEyes.Proc.MapArt
{
    /// <summary>Una texture in memoria, RGBA a byte, riga 0 in basso (come le texture di Unity).</summary>
    public sealed class PixelMap
    {
        public readonly int w, h;
        public readonly byte[] rgba;
        public PixelMap(int w, int h) { this.w = w; this.h = h; rgba = new byte[w * h * 4]; }
        public void Set(int x, int y, float r, float g, float b, float a)
        {
            int i = (y * w + x) * 4;
            rgba[i] = ToByte(r); rgba[i + 1] = ToByte(g); rgba[i + 2] = ToByte(b); rgba[i + 3] = ToByte(a);
        }
        public static byte ToByte(float v) => (byte)(v <= 0f ? 0 : v >= 1f ? 255 : (int)(v * 255f + 0.5f));
        public float A(int x, int y) => rgba[(y * w + x) * 4 + 3] / 255f;

        /// <summary>Campiona con interpolazione bilineare; 'wrap' ripete la texture, altrimenti il bordo si allunga. Colore e alfa in 0..1.</summary>
        public void Sample(float u, float v, bool wrap, out float r, out float g, out float b, out float a)
        {
            float fx = u * w - 0.5f, fy = v * h - 0.5f;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            float r0 = 0, g0 = 0, b0 = 0, a0 = 0;
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    int xx = x0 + i, yy = y0 + j;
                    if (wrap) { xx = ((xx % w) + w) % w; yy = ((yy % h) + h) % h; }
                    else { xx = xx < 0 ? 0 : xx >= w ? w - 1 : xx; yy = yy < 0 ? 0 : yy >= h ? h - 1 : yy; }
                    float k = (i == 0 ? 1f - tx : tx) * (j == 0 ? 1f - ty : ty);
                    int p = (yy * w + xx) * 4;
                    r0 += rgba[p] * k; g0 += rgba[p + 1] * k; b0 += rgba[p + 2] * k; a0 += rgba[p + 3] * k;
                }
            r = r0 / 255f; g = g0 / 255f; b = b0 / 255f; a = a0 / 255f;
        }
    }

    /// <summary>Le texture della mappa, tutte generate dal codice (nessun file): la stessa carta esce uguale in ogni macchina. Si costruiscono una
    /// volta sola, la prima volta che un HUD crea la sua mappa (MapAssets.Ensure); quanto costano lo dice MapAssets.BuildMs.</summary>
    public static class MapTextures
    {
        /// <summary>La texture dei nastri: 4 x 64, bianca, con l'alfa a campana sulla larghezza (Pen.Plateau e' la parte piena).</summary>
        public static PixelMap LineTex()
        {
            var t = new PixelMap(4, 64);
            for (int y = 0; y < 64; y++)
            {
                float v = (y + 0.5f) / 64f;
                float edge = 0.5f * (1f - Pen.Plateau);                     // 0,22
                float a = Math.Min(Noise.Smooth(Math.Min(1f, v / edge)), Noise.Smooth(Math.Min(1f, (1f - v) / edge)));
                for (int x = 0; x < 4; x++) t.Set(x, y, 1f, 1f, 1f, a);
            }
            return t;
        }

        /// <summary>Acquerello: 256 x 256 che si ripete senza giunte. Il colore e' quasi bianco, l'alfa e' il pigmento macchiato.</summary>
        public static PixelMap WashTex()
        {
            const int N = 256;
            var t = new PixelMap(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float big = Noise.Fbm(u * 5f, v * 5f, 11, 4, 5);
                    float fine = Noise.Fbm(u * 24f, v * 24f, 23, 2, 24);
                    float grain = Noise.R(x, y, 5) - 0.5f;
                    float a = 0.70f + (big - 0.5f) * 1.25f + (fine - 0.5f) * 0.30f + grain * 0.12f;
                    a = Math.Max(0.30f, Math.Min(1f, a));
                    float k = 0.93f + 0.07f * big;
                    t.Set(x, y, k, k, k, a);
                }
            return t;
        }

        /// <summary>Granuli per sporcare (rumore fine, scuro e chiaro), 128 x 128 che si ripete.</summary>
        public static PixelMap GrainTex()
        {
            const int N = 128;
            var t = new PixelMap(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float r = Noise.R(x, y, 77);
                    bool dark = Noise.R(x, y, 78) < 0.62f;
                    float a = r * r * (dark ? 0.55f : 0.35f);
                    t.Set(x, y, dark ? 0f : 1f, dark ? 0f : 1f, dark ? 0f : 1f, a);
                }
            return t;
        }

        /// <summary>Il formato della carta in gioco: 2:1, cosi' una carta sola copre la finestra del foglio grande (larga piu' del doppio dell'alta)
        /// senza stirare le macchie. Il pittore e l'anteprima di planlab usano il 512 x 512 di sempre.</summary>
        public const int PaperW = 1024, PaperH = 512;

        /// <summary>La carta, una volta sola sul foglio. Fibre, nuvole, macchie di foxing, aloni di caffe' e le due pieghe del foglio ripiegato in
        /// quattro. Il colore e' quello della carta chiara: l'illuminazione della lampada la dipinge la mesh con i colori dei vertici. Le frequenze
        /// del rumore e il numero delle macchie seguono i pixel, quindi gli aloni restano tondi anche su una carta larga.</summary>
        public static PixelMap PaperTex(int seed = 3, int w = 512, int h = 512)
        {
            var t = new PixelMap(w, h);
            var rgb = new float[w * h * 3];
            float asp = w / (float)h, area = w * (float)h / (512f * 512f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    float cloud = Noise.Fbm(u * 3.2f * asp, v * 3.2f, seed, 4);
                    float mid = Noise.Fbm(u * 11f * asp, v * 11f, seed + 17, 3);
                    float fine = Noise.R(x, y, seed + 2) - 0.5f;
                    float k = 0.86f + (cloud - 0.5f) * 0.30f + (mid - 0.5f) * 0.10f + fine * 0.05f;
                    int i = (y * w + x) * 3;
                    rgb[i] = 0.93f * k + 0.03f; rgb[i + 1] = 0.83f * k + 0.02f; rgb[i + 2] = 0.62f * k;
                }
            // fibre: tratti corti e chiari o scuri
            int fibres = (int)(1100 * area);
            for (int f = 0; f < fibres; f++)
            {
                float cx = Noise.R(f, seed + 40) * w, cy = Noise.R(f, seed + 41) * h, ang = Noise.R(f, seed + 42) * 6.2831853f, len = 5f + Noise.R(f, seed + 43) * 18f;
                float tone = Noise.R(f, seed + 44) < 0.5f ? -0.06f : 0.05f;
                for (float s = -len * 0.5f; s <= len * 0.5f; s += 0.7f)
                    Blend(rgb, w, h, cx + (float)Math.Cos(ang) * s, cy + (float)Math.Sin(ang) * s, tone, 0.9f);
            }
            // foxing: puntini bruni con alone
            int spots = (int)(70 * area);
            for (int f = 0; f < spots; f++)
            {
                float cx = Noise.R(f, seed + 50) * w, cy = Noise.R(f, seed + 51) * h, rad = 1f + Noise.R(f, seed + 52) * 3.4f;
                SoftSpot(rgb, w, h, cx, cy, rad, 0.20f, 0.46f, 0.30f, 0.16f);
            }
            // aloni (tazza di caffe', acqua): un anello scuro e un interno appena piu' scuro
            int rings = Math.Max(3, (int)Math.Round(3f * area));
            for (int f = 0; f < rings; f++)
            {
                float cx = (0.18f + Noise.R(f, seed + 60) * 0.64f) * w, cy = (0.18f + Noise.R(f, seed + 61) * 0.64f) * h, rad = 36f + Noise.R(f, seed + 62) * 40f;
                Halo(rgb, w, h, cx, cy, rad, 0.10f + 0.04f * (f % 4));
            }
            // pieghe: una orizzontale e una verticale, quasi al centro (il foglio e' una mappa piegata)
            float foldV = 0.5f + (Noise.R(1, seed + 70) - 0.5f) * 0.04f, foldH = 0.5f + (Noise.R(2, seed + 70) - 0.5f) * 0.04f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    float dv = Math.Abs(u - foldV) * w, dh = Math.Abs(v - foldH) * h;
                    float crease = Math.Max(Fold(dv), Fold(dh));
                    // il bordo scurito (carta di te') e' largo il 10% dell'altezza su tutti e quattro i lati
                    float edge = Math.Min(Math.Min(u * w, (1f - u) * w), Math.Min(v * h, (1f - v) * h)) / h;
                    float tea = 1f - 0.34f * Noise.Smooth(Math.Max(0f, 1f - edge / 0.10f)) * (0.7f + 0.6f * Noise.Fbm(u * 6f * asp, v * 6f, seed + 80, 3));
                    int i = (y * w + x) * 3;
                    float k = tea * (1f - crease);
                    t.Set(x, y, rgb[i] * k, rgb[i + 1] * k, rgb[i + 2] * k * (1f + 0.04f * (1f - tea)), 1f);
                }
            return t;
        }

        // una piega: una riga scura stretta con un lato appena piu' chiaro
        static float Fold(float dist) => dist < 1.2f ? 0.20f * (1f - dist / 1.2f) : dist < 3.5f ? -0.04f * (1f - (dist - 1.2f) / 2.3f) : 0f;

        static void Blend(float[] rgb, int w, int h, float x, float y, float tone, float k)
        {
            int ix = (int)x, iy = (int)y;
            if (ix < 0 || iy < 0 || ix >= w || iy >= h) return;
            int i = (iy * w + ix) * 3;
            rgb[i] += tone * k; rgb[i + 1] += tone * k * 0.95f; rgb[i + 2] += tone * k * 0.85f;
        }

        static void SoftSpot(float[] rgb, int w, int h, float cx, float cy, float rad, float r, float g, float b, float alpha)
        {
            int x0 = Math.Max(0, (int)(cx - rad * 2.2f)), x1 = Math.Min(w - 1, (int)(cx + rad * 2.2f));
            int y0 = Math.Max(0, (int)(cy - rad * 2.2f)), y1 = Math.Min(h - 1, (int)(cy + rad * 2.2f));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / rad;
                    float a = alpha * (d < 1f ? 1f : Math.Max(0f, 1f - (d - 1f) / 1.2f) * 0.5f);
                    if (a <= 0f) continue;
                    int i = (y * w + x) * 3;
                    rgb[i] += (r - rgb[i]) * a; rgb[i + 1] += (g - rgb[i + 1]) * a; rgb[i + 2] += (b - rgb[i + 2]) * a;
                }
        }

        static void Halo(float[] rgb, int w, int h, float cx, float cy, float rad, float strength)
        {
            int x0 = Math.Max(0, (int)(cx - rad * 1.4f)), x1 = Math.Min(w - 1, (int)(cx + rad * 1.4f));
            int y0 = Math.Max(0, (int)(cy - rad * 1.4f)), y1 = Math.Min(h - 1, (int)(cy + rad * 1.4f));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float ang = (float)Math.Atan2(dy, dx);
                    float rr = rad * (1f + 0.06f * Noise.V1(ang * 2.5f + 3f, 91));
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    float ring = (float)Math.Exp(-((d - rr) * (d - rr)) / 9f);
                    float inside = d < rr ? 0.35f : 0f;
                    float a = strength * (ring + inside * 0.4f);
                    int i = (y * w + x) * 3;
                    rgb[i] *= 1f - a * 0.55f; rgb[i + 1] *= 1f - a * 0.70f; rgb[i + 2] *= 1f - a * 0.95f;
                }
        }

        // ------------------------------------------------------------------ macchie (sangue, inchiostro, graffi)
        /// <summary>Atlante di macchie, 512 x 256 = otto celle di 128: 0-2 schizzi di sangue, 3-4 colate, 5 macchia d'inchiostro, 6 anello di
        /// caffe', 7 graffi. Bianche con l'alfa: il colore lo mette la mesh.</summary>
        public const int StainCells = 8;

        public static PixelMap StainAtlas()
        {
            var t = new PixelMap(512, 256);
            for (int c = 0; c < StainCells; c++) PaintStain(t, c);
            return t;
        }

        /// <summary>Il rettangolo UV (x0, y0, x1, y1) della cella.</summary>
        public static void StainUv(int cell, out float u0, out float v0, out float u1, out float v1)
        {
            int cx = cell % 4, cy = cell / 4;
            const float pad = 0.5f / 512f;
            u0 = cx * 0.25f + pad; u1 = (cx + 1) * 0.25f - pad;
            v0 = cy * 0.5f + pad; v1 = (cy + 1) * 0.5f - pad;
        }

        static void PaintStain(PixelMap t, int cell)
        {
            const int S = 128;
            int ox = (cell % 4) * S, oy = (cell / 4) * S;
            var a = new float[S * S];
            float C = S * 0.5f;
            switch (cell)
            {
                case 0: case 1: case 2:
                {
                    float r0 = 17f + cell * 3f;
                    // il corpo dello schizzo
                    for (int y = 0; y < S; y++)
                        for (int x = 0; x < S; x++)
                        {
                            float dx = x - C, dy = y - C, d = (float)Math.Sqrt(dx * dx + dy * dy), ang = (float)Math.Atan2(dy, dx);
                            float rr = r0 * (1f + 0.30f * Noise.V1(ang * 1.9f + cell * 5f, 301 + cell) + 0.12f * Noise.V1(ang * 5.1f, 311 + cell));
                            a[y * S + x] = Math.Max(a[y * S + x], Mask(rr - d));
                        }
                    // raggi che escono dal corpo
                    int rays = 9 + cell * 2;
                    for (int k = 0; k < rays; k++)
                    {
                        float ang = Noise.R(k, 320 + cell) * 6.2831853f, len = 26f + Noise.R(k, 330 + cell) * 30f, w0 = 3.2f + Noise.R(k, 340 + cell) * 2f;
                        for (float s = r0 * 0.6f; s < r0 + len; s += 0.8f)
                        {
                            float f = 1f - (s - r0 * 0.6f) / (r0 + len - r0 * 0.6f);
                            Spot(a, S, C + (float)Math.Cos(ang) * s, C + (float)Math.Sin(ang) * s, w0 * f * f + 0.6f);
                        }
                        // la goccia in punta
                        Spot(a, S, C + (float)Math.Cos(ang) * (r0 + len + 3f), C + (float)Math.Sin(ang) * (r0 + len + 3f), w0 * 0.55f + 0.6f);
                    }
                    // schizzi sparsi
                    for (int k = 0; k < 26; k++)
                    {
                        float ang = Noise.R(k, 350 + cell) * 6.2831853f, dist = r0 + 10f + Noise.R(k, 360 + cell) * 36f;
                        Spot(a, S, C + (float)Math.Cos(ang) * dist, C + (float)Math.Sin(ang) * dist, 0.9f + Noise.R(k, 370 + cell) * 2.4f);
                    }
                    break;
                }
                case 3: case 4:
                {
                    // una colata dall'alto: un bordo scuro in alto e tre colate che scendono, ognuna con la sua goccia in fondo
                    for (int y = 0; y < S; y++)
                        for (int x = 0; x < S; x++)
                        {
                            float top = 6f + 4f * Noise.V1(x * 0.25f, 380 + cell);
                            if (y >= S - 1 - (int)top) a[y * S + x] = 0.95f;     // la riga 0 e' in basso: l'alto della cella sta in fondo all'array
                        }
                    int drips = 3 + (cell == 4 ? 1 : 0);
                    for (int k = 0; k < drips; k++)
                    {
                        float x = 20f + (k + Noise.R(k, 390 + cell)) * (S - 40f) / drips, len = 30f + Noise.R(k, 400 + cell) * 70f, w = 3.4f + Noise.R(k, 410 + cell) * 2.4f;
                        float yTop = S - 8f;
                        for (float s = 0f; s < len; s += 0.7f)
                        {
                            float wobble = (float)Math.Sin(s * 0.11f + k) * 0.8f;
                            Spot(a, S, x + wobble, yTop - s, w * (1f - 0.35f * s / len));
                        }
                        Spot(a, S, x, yTop - len, w * 1.25f);
                    }
                    break;
                }
                case 5:
                {
                    for (int y = 0; y < S; y++)
                        for (int x = 0; x < S; x++)
                        {
                            float dx = x - C, dy = y - C, d = (float)Math.Sqrt(dx * dx + dy * dy), ang = (float)Math.Atan2(dy, dx);
                            float rr = 34f * (1f + 0.14f * Noise.V1(ang * 2.2f, 421) + 0.07f * Noise.V1(ang * 6f, 422));
                            a[y * S + x] = Mask(rr - d) * (0.85f + 0.15f * Noise.V2d(x * 0.15f, y * 0.15f, 423));
                        }
                    for (int k = 0; k < 8; k++)
                    {
                        float ang = Noise.R(k, 430) * 6.2831853f, dist = 42f + Noise.R(k, 431) * 12f;
                        Spot(a, S, C + (float)Math.Cos(ang) * dist, C + (float)Math.Sin(ang) * dist, 1.2f + Noise.R(k, 432) * 2.2f);
                    }
                    break;
                }
                case 6:
                {
                    for (int y = 0; y < S; y++)
                        for (int x = 0; x < S; x++)
                        {
                            float dx = x - C, dy = y - C, d = (float)Math.Sqrt(dx * dx + dy * dy), ang = (float)Math.Atan2(dy, dx);
                            float rr = 40f * (1f + 0.04f * Noise.V1(ang * 3f, 441));
                            float ring = (float)Math.Exp(-((d - rr) * (d - rr)) / 7f);
                            float body = d < rr ? 0.18f : 0f;
                            float gap = Noise.V1(ang * 2f + 4f, 442) > 0.55f ? 0.35f : 1f;      // l'anello non e' chiuso del tutto
                            a[y * S + x] = Math.Min(1f, ring * 0.85f * gap + body);
                        }
                    break;
                }
                default:
                {
                    // graffi: quattro tagli quasi paralleli, piu' larghi al centro
                    for (int k = 0; k < 4; k++)
                    {
                        float off = (k - 1.5f) * 11f;
                        for (float s = -48f; s <= 48f; s += 0.6f)
                        {
                            float taper = 1f - (float)Math.Abs(s) / 52f;
                            float x = C + s * 0.62f - off * 0.78f, y = C + s * 0.78f + off * 0.62f + (float)Math.Sin(s * 0.09f + k) * 1.2f;
                            Spot(a, S, x, y, 0.5f + 1.7f * taper);
                        }
                    }
                    break;
                }
            }
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++) t.Set(ox + x, oy + y, 1f, 1f, 1f, Math.Min(1f, a[y * S + x]));
        }

        // il bordo morbido di una forma: dentro vale 1, fuori 0, con mezzo pixel di sfumatura
        static float Mask(float signedDist) => Math.Max(0f, Math.Min(1f, signedDist + 0.5f));

        static void Spot(float[] a, int s, float cx, float cy, float rad)
        {
            int x0 = Math.Max(0, (int)(cx - rad - 1f)), x1 = Math.Min(s - 1, (int)(cx + rad + 1f));
            int y0 = Math.Max(0, (int)(cy - rad - 1f)), y1 = Math.Min(s - 1, (int)(cy + rad + 1f));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float v = Mask(rad - d);
                    if (v > a[y * s + x]) a[y * s + x] = v;
                }
        }
    }
}
