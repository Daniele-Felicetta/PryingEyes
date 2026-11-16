using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>La doppia pagina del grimorio, dipinta a codice (nessun file), ispirata alle due tavole dell'utente: pergamena macchiata con
    /// i bordi strappati e bruciati, la piega scura in mezzo, il sangue che cola dal bordo alto, righe di scrittura sbiadita, i nastri dei
    /// titoli in cima alle due pagine, pentacoli negli angoli e l'anello di rune rosse attorno al dodecaedro. La stessa disposizione la
    /// usa GrimoireUI (coordinate normalizzate della pagina: u da sinistra, v dall'alto) e il libro aperto sull'altare (con i nodi gia'
    /// stampati e una maschera di luce per le rune).</summary>
    public static class GrimoirePage
    {
        public const int W = 1600, H = 900;
        /// <summary>Centro del dodecaedro e raggi (in frazioni dell'altezza) dell'anello esterno, di quello interno e delle rune.</summary>
        public static readonly Vector2 GraphCenter = new Vector2(0.625f, 0.535f);
        public const float R1 = 0.315f, R2 = 0.165f, RRunes = 0.395f;
        public static readonly Rect LeftPage = new Rect(0.028f, 0.045f, 0.462f, 0.91f), RightPage = new Rect(0.51f, 0.045f, 0.462f, 0.91f);
        public static readonly Rect TitleLeft = new Rect(0.07f, 0.055f, 0.36f, 0.115f), TitleRight = new Rect(0.57f, 0.055f, 0.36f, 0.115f);

        static readonly Dictionary<Grimoire.Book, Texture2D> spreads = new Dictionary<Grimoire.Book, Texture2D>();
        static readonly Dictionary<Grimoire.Book, Texture2D> pages3d = new Dictionary<Grimoire.Book, Texture2D>();
        static readonly Dictionary<Grimoire.Book, Texture2D> glows = new Dictionary<Grimoire.Book, Texture2D>();

        /// <summary>Posizione (normalizzata, v dall'alto) del nodo i nel disegno: interni a esagono (il primo in alto, in senso orario),
        /// i due esterni di ogni interno ai suoi lati sull'anello esterno, il supremo al centro.</summary>
        public static Vector2 NodePos(int i)
        {
            float aspect = (float)H / W;
            if (i == Grimoire.Supreme) return GraphCenter;
            float ang, r;
            if (i >= Grimoire.FirstInner) { int k = i - 12; ang = 90f - 60f * k; r = R2; }
            else { int k = i / 2; ang = 90f - 60f * k + (i % 2 == 0 ? 15f : -15f); r = R1; }
            float a = ang * Mathf.Deg2Rad;
            return new Vector2(GraphCenter.x + Mathf.Cos(a) * r * aspect, GraphCenter.y - Mathf.Sin(a) * r);
        }

        // ------------------------------------------------------------------ rumore
        static float[] lattice;
        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }
        static float Noise(float x, float y)
        {
            if (lattice == null) { lattice = new float[256 * 256]; for (int j = 0; j < 256; j++) for (int i = 0; i < 256; i++) lattice[j * 256 + i] = Hash(i, j); }
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int x0 = xi & 255, y0 = yi & 255, x1 = (x0 + 1) & 255, y1 = (y0 + 1) & 255;
            float a = lattice[y0 * 256 + x0], b = lattice[y0 * 256 + x1], c = lattice[y1 * 256 + x0], d = lattice[y1 * 256 + x1];
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
        static float Fbm(float x, float y) => Noise(x, y) * 0.55f + Noise(x * 2.1f + 17f, y * 2.1f + 5f) * 0.28f + Noise(x * 4.3f + 3f, y * 4.3f + 29f) * 0.17f;

        // ------------------------------------------------------------------ pittura
        sealed class Canvas
        {
            public readonly Color[] px = new Color[W * H];
            public void Blend(int x, int y, Color c, float a)
            {
                if (x < 0 || y < 0 || x >= W || y >= H || a <= 0f) return;
                int i = (H - 1 - y) * W + x;               // y dall'alto: la riga 0 della texture e' in basso
                var d = px[i];
                float oa = d.a + a * (1f - d.a);
                px[i] = new Color(Mathf.Lerp(d.r, c.r, a), Mathf.Lerp(d.g, c.g, a), Mathf.Lerp(d.b, c.b, a), oa);
            }
            public void Disc(float cx, float cy, float r, Color c, float a)
            {
                int x0 = Mathf.FloorToInt(cx - r - 1), x1 = Mathf.CeilToInt(cx + r + 1), y0 = Mathf.FloorToInt(cy - r - 1), y1 = Mathf.CeilToInt(cy + r + 1);
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    Blend(x, y, c, a * Mathf.Clamp01(r + 0.5f - d));
                }
            }
            public void Line(float ax, float ay, float bx, float by, float w, Color c, float a)
            {
                int x0 = Mathf.FloorToInt(Mathf.Min(ax, bx) - w - 1), x1 = Mathf.CeilToInt(Mathf.Max(ax, bx) + w + 1);
                int y0 = Mathf.FloorToInt(Mathf.Min(ay, by) - w - 1), y1 = Mathf.CeilToInt(Mathf.Max(ay, by) + w + 1);
                float dx = bx - ax, dy = by - ay, l2 = Mathf.Max(1e-4f, dx * dx + dy * dy);
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    float t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / l2);
                    float qx = ax + dx * t - x, qy = ay + dy * t - y;
                    Blend(x, y, c, a * Mathf.Clamp01(w * 0.5f + 0.5f - Mathf.Sqrt(qx * qx + qy * qy)));
                }
            }
            public void Ring(float cx, float cy, float r, float w, Color c, float a, int segs = 160)
            {
                for (int i = 0; i < segs; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                    Line(cx + Mathf.Cos(a0) * r, cy + Mathf.Sin(a0) * r, cx + Mathf.Cos(a1) * r, cy + Mathf.Sin(a1) * r, w, c, a);
                }
            }
            public void Star(float cx, float cy, float r, int n, int step, float w, Color c, float a, float rot = -90f)
            {
                for (int i = 0; i < n; i++)
                {
                    float a0 = (rot + i * 360f / n) * Mathf.Deg2Rad, a1 = (rot + (i + step) * 360f / n) * Mathf.Deg2Rad;
                    Line(cx + Mathf.Cos(a0) * r, cy + Mathf.Sin(a0) * r, cx + Mathf.Cos(a1) * r, cy + Mathf.Sin(a1) * r, w, c, a);
                }
            }
        }

        static readonly Color Ink = new Color(0.20f, 0.10f, 0.06f), RedInk = new Color(0.55f, 0.05f, 0.03f), BloodC = new Color(0.36f, 0.015f, 0.01f);

        /// <summary>La pergamena di una pagina: tono, macchie, bordo strappato e bruciato. Ritorna alfa 0 fuori dal foglio.</summary>
        static Color Parchment(int x, int y, int seed)
        {
            float u = x / (float)W, v = y / (float)H;
            bool left = u < 0.5f;
            var pg = left ? LeftPage : RightPage;
            // distanza dal bordo del foglio (in pixel), col bordo strappato dal rumore
            float dl = (u - pg.xMin) * W, dr = (pg.xMax - u) * W, dt = (v - pg.yMin) * H, db = (pg.yMax - v) * H;
            float torn = (Fbm(x * 0.045f + seed, y * 0.045f) - 0.5f) * 26f + (Noise(x * 0.25f, y * 0.25f) - 0.5f) * 5f;
            float edge = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(dt, db)) + torn;
            // la piega: niente strappi verso il centro (il foglio entra nella legatura)
            if (left ? dr < 30f : dl < 30f) edge = Mathf.Min(Mathf.Min(left ? dl : dr, dt), db) + torn;
            if (edge < 0f) return new Color(0f, 0f, 0f, 0f);
            float n = Fbm(x * 0.012f + seed * 3f, y * 0.012f);
            float stains = Mathf.Clamp01((Fbm(x * 0.006f + 50f, y * 0.006f + seed) - 0.52f) * 4f);
            var c = Color.Lerp(new Color(0.84f, 0.74f, 0.55f), new Color(0.66f, 0.53f, 0.36f), n * 0.9f);
            c = Color.Lerp(c, new Color(0.47f, 0.33f, 0.20f), stains * 0.5f);
            c *= 0.92f + Noise(x * 0.9f, y * 0.9f) * 0.12f;               // grana della carta
            // bruciatura ai bordi: marrone, poi nero, poi il foglio finisce
            float burn = 1f - Mathf.Clamp01(edge / 34f);
            c = Color.Lerp(c, new Color(0.32f, 0.18f, 0.08f), burn * burn * 0.9f);
            c = Color.Lerp(c, new Color(0.05f, 0.03f, 0.02f), Mathf.Clamp01((burn - 0.75f) * 4f));
            // ombra della piega in mezzo e degli angoli
            float gutter = Mathf.Abs(u - 0.5f) * W;
            c *= Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(gutter / 80f));
            float vig = Mathf.Clamp01(1f - ((u - 0.5f) * (u - 0.5f) * 1.3f + (v - 0.5f) * (v - 0.5f) * 1.6f));
            c *= Mathf.Lerp(0.72f, 1f, vig);
            c.a = Mathf.Clamp01(edge / 1.5f);
            return c;
        }

        static void Script(Canvas cv, System.Random r, float x0, float y0, float x1, float y1, float alpha)
        {
            for (float y = y0; y < y1; y += 15f)
            {
                float x = x0 + (float)r.NextDouble() * 10f;
                while (x < x1 - 10f)
                {
                    float wlen = 14f + (float)r.NextDouble() * 34f;
                    float px = x, py = y;
                    for (float t = 0; t < wlen; t += 1.5f)
                    {
                        float nx = x + t, ny = y + Mathf.Sin(t * 0.9f + (float)r.NextDouble()) * 2.6f - (Mathf.Sin(t * 0.35f) > 0.85f ? 4f : 0f);
                        cv.Line(px, py, nx, ny, 1.1f, Ink, alpha);
                        px = nx; py = ny;
                    }
                    x += wlen + 6f + (float)r.NextDouble() * 6f;
                }
            }
        }

        static void Banner(Canvas cv, Rect n)
        {
            float x0 = n.xMin * W, x1 = n.xMax * W, y0 = n.yMin * H, y1 = n.yMax * H;
            for (int y = (int)y0; y < (int)y1; y++)
                for (int x = (int)x0; x < (int)x1; x++)
                {
                    float u = (x - x0) / (x1 - x0), v = (y - y0) / (y1 - y0);
                    float wave = Mathf.Sin(u * Mathf.PI * 2f) * 0.06f;
                    if (v < 0.08f + wave || v > 0.92f + wave) continue;
                    var c = Color.Lerp(new Color(0.88f, 0.80f, 0.62f), new Color(0.70f, 0.58f, 0.40f), Fbm(x * 0.03f, y * 0.03f));
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u) * (x1 - x0), Mathf.Min(v - 0.08f - wave, 0.92f + wave - v) * (y1 - y0));
                    c = Color.Lerp(new Color(0.30f, 0.17f, 0.08f), c, Mathf.Clamp01(edge / 6f));
                    cv.Blend(x, y, c, 0.96f);
                }
            // le estremita' arrotolate
            foreach (float ex in new[] { x0, x1 })
                for (int k = 0; k < 3; k++) cv.Disc(ex, (y0 + y1) * 0.5f + (k - 1) * 9f, 7f - k, new Color(0.45f, 0.30f, 0.16f), 0.9f);
        }

        static void Blood(Canvas cv, System.Random r)
        {
            // colature dal bordo alto delle pagine
            for (int i = 0; i < 26; i++)
            {
                float x = (float)(LeftPage.xMin + r.NextDouble() * (RightPage.xMax - LeftPage.xMin)) * W;
                if (Mathf.Abs(x - W * 0.5f) < 40f) continue;
                float y0 = LeftPage.yMin * H + 6f, len = 30f + (float)r.NextDouble() * (r.NextDouble() < 0.25 ? 260f : 110f), w = 3f + (float)r.NextDouble() * 6f;
                for (float y = y0; y < y0 + len; y += 1f)
                {
                    float taper = 1f - (y - y0) / len * 0.5f;
                    cv.Line(x + Mathf.Sin(y * 0.03f) * 1.5f, y, x + Mathf.Sin((y + 1) * 0.03f) * 1.5f, y + 1f, w * taper, BloodC, 0.85f);
                }
                cv.Disc(x, y0 + len, w * 0.75f, BloodC, 0.9f);
                cv.Disc(x, y0 + 3f, w * 1.3f, BloodC, 0.8f);
            }
            // schizzi
            for (int i = 0; i < 40; i++)
            {
                float x = (float)r.NextDouble() * W, y = (float)r.NextDouble() * H;
                float s = 1.5f + (float)r.NextDouble() * (r.NextDouble() < 0.1 ? 14f : 4f);
                cv.Disc(x, y, s, BloodC, 0.55f);
                for (int k = 0; k < 4; k++) cv.Disc(x + ((float)r.NextDouble() - 0.5f) * s * 6f, y + ((float)r.NextDouble() - 0.5f) * s * 6f, s * 0.3f, BloodC, 0.5f);
            }
        }

        static void Pentacle(Canvas cv, float cx, float cy, float r, Color c, float a)
        {
            cv.Ring(cx, cy, r, 2f, c, a, 90);
            cv.Ring(cx, cy, r * 0.88f, 1.2f, c, a * 0.8f, 90);
            cv.Star(cx, cy, r * 0.86f, 5, 2, 1.6f, c, a);
        }

        static void Runes(Canvas cv, System.Random r, float cx, float cy, float rr, Color c, float a)
        {
            cv.Ring(cx, cy, rr + 22f, 2.2f, c, a);
            cv.Ring(cx, cy, rr - 22f, 2.2f, c, a);
            int n = 34;
            for (int i = 0; i < n; i++)
            {
                float ang = i * Mathf.PI * 2f / n;
                float gx = cx + Mathf.Cos(ang) * rr, gy = cy + Mathf.Sin(ang) * rr;
                Vector2 tang = new Vector2(-Mathf.Sin(ang), Mathf.Cos(ang)), rad = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                int strokes = 2 + r.Next(3);
                for (int s = 0; s < strokes; s++)
                {
                    Vector2 p0 = new Vector2(gx, gy) + tang * (((float)r.NextDouble() - 0.5f) * 14f) + rad * (((float)r.NextDouble() - 0.5f) * 22f);
                    Vector2 p1 = new Vector2(gx, gy) + tang * (((float)r.NextDouble() - 0.5f) * 14f) + rad * (((float)r.NextDouble() - 0.5f) * 22f);
                    cv.Line(p0.x, p0.y, p1.x, p1.y, 2.2f, c, a);
                }
            }
        }

        /// <summary>La doppia pagina vuota (senza nodi): sfondo trasparente, per l'interfaccia.</summary>
        public static Texture2D Spread(Grimoire.Book book)
        {
            if (spreads.TryGetValue(book, out var t) && t != null) return t;
            var cv = Paint(book, out _);
            t = Bake(cv, "Grimorio_" + book);
            spreads[book] = t;
            return t;
        }

        static Canvas Paint(Grimoire.Book book, out Canvas glow)
        {
            int seed = book == Grimoire.Book.Stregoneria ? 11 : 37;
            var r = new System.Random(seed);
            var cv = new Canvas();
            glow = new Canvas();
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                {
                    var c = Parchment(x, y, seed);
                    if (c.a > 0f) cv.px[(H - 1 - y) * W + x] = c;
                }
            // la scrittura sbiadita: colonne nei margini delle due pagine
            Script(cv, r, LeftPage.xMin * W + 40f, H * 0.62f, LeftPage.xMin * W + 300f, H * 0.90f, 0.20f);
            Script(cv, r, RightPage.xMax * W - 230f, H * 0.18f, RightPage.xMax * W - 40f, H * 0.42f, 0.22f);
            Script(cv, r, RightPage.xMax * W - 220f, H * 0.66f, RightPage.xMax * W - 40f, H * 0.88f, 0.22f);
            // pentacoli negli angoli e nel margine destro
            Pentacle(cv, LeftPage.xMax * W - 70f, H * 0.89f, 44f, RedInk, 0.45f);
            Pentacle(cv, RightPage.xMax * W - 120f, H * 0.53f, 74f, RedInk, 0.55f);
            Pentacle(glow, RightPage.xMax * W - 120f, H * 0.53f, 74f, Color.white, 0.8f);
            Pentacle(cv, RightPage.xMax * W - 70f, H * 0.91f, 40f, RedInk, 0.4f);
            // l'anello di rune attorno al dodecaedro e la stella interna che unisce gli esterni (il disegno della tavola)
            float gx = GraphCenter.x * W, gy = GraphCenter.y * H;
            Runes(cv, r, gx, gy, RRunes * H, RedInk, 0.6f);
            Runes(glow, new System.Random(seed), gx, gy, RRunes * H, Color.white, 0.9f);
            cv.Star(gx, gy, R1 * H, 12, 5, 1.6f, RedInk, 0.22f, -75f);
            glow.Star(gx, gy, R1 * H, 12, 5, 1.6f, Color.white, 0.4f, -75f);
            cv.Star(gx, gy, R2 * H * 0.98f, 6, 2, 1.8f, RedInk, 0.3f);
            glow.Star(gx, gy, R2 * H * 0.98f, 6, 2, 1.8f, Color.white, 0.5f);
            // i nastri dei titoli e il sangue
            Banner(cv, TitleLeft);
            Banner(cv, TitleRight);
            Blood(cv, r);
            return cv;
        }

        static Texture2D Bake(Canvas cv, string name, bool readable = false)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels(cv.px);
            t.Apply(true, !readable);
            return t;
        }

        /// <summary>Le pagine per il libro aperto sull'altare: la doppia pagina con i nodi gia' stampati (linee, medaglioni, icone) e la
        /// maschera di luce delle rune (rossa).</summary>
        public static Texture2D Page3D(Grimoire.Book book, out Texture2D glowTex)
        {
            if (pages3d.TryGetValue(book, out var t) && t != null) { glowTex = glows[book]; return t; }
            var cv = Paint(book, out var glow);
            // le linee del dodecaedro e i nodi
            for (int i = 0; i < Grimoire.Count; i++)
                foreach (int j in Grimoire.Neighbors(i))
                {
                    if (j < i) continue;
                    Vector2 a = NodePos(i), b = NodePos(j);
                    cv.Line(a.x * W, a.y * H, b.x * W, b.y * H, 3f, new Color(0.42f, 0.10f, 0.04f), 0.75f);
                    glow.Line(a.x * W, a.y * H, b.x * W, b.y * H, 3f, Color.white, 0.55f);
                }
            var nodes = Grimoire.Of(book);
            foreach (var n in nodes)
            {
                Vector2 p = NodePos(n.index);
                float rad = n.tier == Grimoire.Tier.Supremo ? 54f : n.tier == Grimoire.Tier.Interno ? 40f : 34f;
                cv.Disc(p.x * W, p.y * H, rad + 6f, new Color(0.22f, 0.12f, 0.06f), 0.95f);
                cv.Disc(p.x * W, p.y * H, rad, new Color(0.10f, 0.03f, 0.03f), 0.95f);
                cv.Ring(p.x * W, p.y * H, rad + 3f, 2f, new Color(0.62f, 0.38f, 0.16f), 0.9f, 48);
                glow.Ring(p.x * W, p.y * H, rad + 3f, 2f, Color.white, 0.7f, 48);
                StampIcon(cv, glow, GrimoireIcons.Get(n.icon), p.x * W, p.y * H, rad * 1.45f);
            }
            t = Bake(cv, "GrimorioAltare_" + book);
            var g = Bake(glow, "GrimorioLuce_" + book);
            pages3d[book] = t; glows[book] = g;
            glowTex = g;
            return t;
        }

        static void StampIcon(Canvas cv, Canvas glow, Sprite s, float cx, float cy, float size)
        {
            if (s == null || s.texture == null || !s.texture.isReadable) return;
            var tex = s.texture;
            int n = Mathf.RoundToInt(size);
            var ivory = new Color(0.92f, 0.84f, 0.68f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = tex.GetPixelBilinear((x + 0.5f) / n, 1f - (y + 0.5f) / n).a;
                    if (a <= 0.01f) continue;
                    int px = Mathf.RoundToInt(cx - n * 0.5f + x), py = Mathf.RoundToInt(cy - n * 0.5f + y);
                    cv.Blend(px, py, ivory, a);
                    glow.Blend(px, py, Color.white, a * 0.6f);
                }
        }
    }
}
