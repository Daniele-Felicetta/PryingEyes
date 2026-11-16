using System.Threading.Tasks;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Le trame degli effetti delle magie (MagicVfx), fatte a runtime come il resto del gioco (niente file): una fiamma e un
    /// fumo animati (8 x 8 fotogrammi da 128 px, in ciclo continuo), il bagliore, la scintilla, l'anello dell'onda, la scheggia di
    /// ghiaccio, la goccia, sedici rune, il cerchio del Dodecaedro con l'occhio, il profilo dei fulmini e il nastro del sangue.
    /// I pixel si calcolano in un thread a parte (Begin, al caricamento del giocatore: meno di un secondo); le Texture2D si creano sul
    /// thread principale alla prima richiesta (Get). Rumore: Perlin migliorato, periodico, cosi' fiamma e fumo tornano su se stessi.</summary>
    public static class VfxTextures
    {
        public enum Id { Fire, Smoke, Glow, Spark, Ring, Shard, Drop, Runes, Circle, Beam, Ribbon, Count }
        /// <summary>Fotogrammi per lato di fiamma e fumo, glifi per lato delle rune.</summary>
        public const int SheetCols = 8, RuneCols = 4;

        sealed class Px { public int w, h; public Color32[] data; public bool mips; public TextureWrapMode wrap = TextureWrapMode.Clamp; }

        static Task<Px[]> job;
        static Texture2D[] tex;

        /// <summary>Fa partire il calcolo dei pixel in un thread a parte (si puo' chiamare quante volte si vuole).</summary>
        public static void Begin()
        {
            if (job == null && tex == null) job = Task.Run(() => MakeAll());
        }

        /// <summary>Le trame sono pronte senza aspettare?</summary>
        public static bool Ready => tex != null || (job != null && job.IsCompleted);

        /// <summary>La trama 'id' (se il calcolo non e' finito, lo aspetta).</summary>
        public static Texture2D Get(Id id)
        {
            Begin();
            if (tex == null)
            {
                Px[] px = job.Result;
                tex = new Texture2D[px.Length];
                for (int i = 0; i < px.Length; i++)
                {
                    var p = px[i];
                    var t = new Texture2D(p.w, p.h, TextureFormat.RGBA32, p.mips, false)
                    { wrapMode = p.wrap, filterMode = FilterMode.Bilinear, anisoLevel = 0, name = "magia_" + (Id)i };
                    t.SetPixels32(p.data);
                    t.Apply(p.mips, true);
                    tex[i] = t;
                }
            }
            return tex[(int)id];
        }

        static Px[] MakeAll()
        {
            var r = new Px[(int)Id.Count];
            r[(int)Id.Fire] = Fire();
            r[(int)Id.Smoke] = Smoke();
            r[(int)Id.Glow] = Simple(64, (x, y) => { float d = x * x + y * y; return new Color(1f, 1f, 1f, Mathf.Exp(-d * 6.5f) * Mathf.Clamp01((1f - Mathf.Sqrt(d)) * 5f)); });
            r[(int)Id.Spark] = Simple(32, (x, y) => { float d = x * x + y * y; return new Color(1f, 1f, 1f, Mathf.Clamp01((Mathf.Exp(-d * 16f) + 0.3f * Mathf.Exp(-d * 3.5f)) * Mathf.Clamp01((1f - Mathf.Sqrt(d)) * 4f))); });
            r[(int)Id.Ring] = Ring();
            r[(int)Id.Shard] = Shard();
            r[(int)Id.Drop] = Drop();
            r[(int)Id.Runes] = Runes();
            r[(int)Id.Circle] = Circle();
            r[(int)Id.Beam] = Beam();
            r[(int)Id.Ribbon] = Ribbon();
            return r;
        }

        // ------------------------------------------------------------------ rumore di Perlin (migliorato), periodico
        static readonly int[] P = Perm();

        static int[] Perm()
        {
            var a = new int[256];
            for (int i = 0; i < 256; i++) a[i] = i;
            var rnd = new System.Random(20261116);
            for (int i = 255; i > 0; i--) { int j = rnd.Next(i + 1); int t = a[i]; a[i] = a[j]; a[j] = t; }
            var p = new int[512];
            for (int i = 0; i < 512; i++) p[i] = a[i & 255];
            return p;
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static float Grad(int h, float x, float y, float z)
        {
            int k = h & 15;
            float u = k < 8 ? x : y, v = k < 4 ? y : (k == 12 || k == 14 ? x : z);
            return ((k & 1) == 0 ? u : -u) + ((k & 2) == 0 ? v : -v);
        }

        static int Wrap(int a, int p) { int m = a % p; return m < 0 ? m + p : m; }

        /// <summary>Rumore in -1..1, periodico in x, y, z con periodi px, py, pz (interi, fino a 256).</summary>
        static float Noise(float x, float y, float z, int px, int py, int pz)
        {
            int X = Mathf.FloorToInt(x), Y = Mathf.FloorToInt(y), Z = Mathf.FloorToInt(z);
            float fx = x - X, fy = y - Y, fz = z - Z;
            int x0 = Wrap(X, px), x1 = Wrap(X + 1, px), y0 = Wrap(Y, py), y1 = Wrap(Y + 1, py), z0 = Wrap(Z, pz), z1 = Wrap(Z + 1, pz);
            float u = Fade(fx), v = Fade(fy), w = Fade(fz);
            int a0 = P[P[x0] + y0], a1 = P[P[x0] + y1], b0 = P[P[x1] + y0], b1 = P[P[x1] + y1];
            float l00 = Mathf.LerpUnclamped(Grad(P[a0 + z0], fx, fy, fz), Grad(P[b0 + z0], fx - 1f, fy, fz), u);
            float l10 = Mathf.LerpUnclamped(Grad(P[a1 + z0], fx, fy - 1f, fz), Grad(P[b1 + z0], fx - 1f, fy - 1f, fz), u);
            float l01 = Mathf.LerpUnclamped(Grad(P[a0 + z1], fx, fy, fz - 1f), Grad(P[b0 + z1], fx - 1f, fy, fz - 1f), u);
            float l11 = Mathf.LerpUnclamped(Grad(P[a1 + z1], fx, fy - 1f, fz - 1f), Grad(P[b1 + z1], fx - 1f, fy - 1f, fz - 1f), u);
            return Mathf.LerpUnclamped(Mathf.LerpUnclamped(l00, l10, v), Mathf.LerpUnclamped(l01, l11, v), w);
        }

        /// <summary>Somma di ottave (frequenza doppia, ampiezza meta'), periodica come Noise: i periodi raddoppiano con la frequenza.</summary>
        static float Fbm(float x, float y, float z, int px, int py, int pz, int oct)
        {
            float s = 0f, a = 0.5f, norm = 0f;
            int f = 1;
            for (int o = 0; o < oct; o++)
            {
                s += a * Noise(x * f, y * f, z * f, Mathf.Min(256, px * f), Mathf.Min(256, py * f), Mathf.Min(256, pz * f));
                norm += a; a *= 0.5f; f *= 2;
            }
            return s / norm;
        }

        static Color32 C(float r, float g, float b, float a) =>
            new Color32((byte)(Mathf.Clamp01(r) * 255f + 0.5f), (byte)(Mathf.Clamp01(g) * 255f + 0.5f), (byte)(Mathf.Clamp01(b) * 255f + 0.5f), (byte)(Mathf.Clamp01(a) * 255f + 0.5f));

        static float Smooth(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        /// <summary>Una trama quadrata da una funzione di (x, y) in -1..1.</summary>
        static Px Simple(int n, System.Func<float, float, Color> f, bool mips = true)
        {
            var px = new Px { w = n, h = n, data = new Color32[n * n], mips = mips };
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    var c = f((i + 0.5f) / n * 2f - 1f, (j + 0.5f) / n * 2f - 1f);
                    px.data[j * n + i] = C(c.r, c.g, c.b, c.a);
                }
            return px;
        }

        // ------------------------------------------------------------------ fiamma
        /// <summary>La lingua di fuoco: una goccia rovesciata (larga alla base, a punta in alto) mangiata da un rumore che sale e da uno
        /// che la piega di lato, piu' in alto piu' forte; il colore viene dalla temperatura (densita' e altezza): bianco-giallo al
        /// cuore, arancio, rosso ai bordi e in punta. 64 fotogrammi in ciclo (il rumore sale di un periodo intero).</summary>
        static Px Fire()
        {
            const int F = 128, N = SheetCols, W = F * N;
            var px = new Px { w = W, h = W, data = new Color32[W * W], mips = true };
            int frames = N * N;
            for (int k = 0; k < frames; k++)
            {
                float tau = (float)k / frames;
                int ox = (k % N) * F, oy = (N - 1 - k / N) * F;      // primo fotogramma in alto a sinistra, come il ParticleSystem
                for (int j = 0; j < F; j++)
                {
                    float v = (j + 0.5f) / F;
                    for (int i = 0; i < F; i++)
                    {
                        float u = (i + 0.5f) / F;
                        float x = (u - 0.5f) * 2f;
                        float yn = v * 3f - tau * 3f, zn = tau * 2f;
                        // la torsione laterale cresce con l'altezza; la base e' tonda e stretta, la punta sottile
                        float warp = Fbm(x * 1.1f + 7.3f, yn + 1.7f, zn, 256, 3, 2, 3);
                        float xs = x + warp * 0.75f * Mathf.Pow(v, 1.2f);
                        float width = (0.55f * Mathf.Pow(Mathf.Max(0f, 1f - v), 0.75f) + 0.03f) * (0.5f + 0.5f * Smooth(0f, 0.24f, v));
                        float q = xs / width;
                        float shape = Mathf.Clamp01(1f - q * q) * Smooth(0f, 0.10f, v);
                        // il rumore che sale, con contrasto: in alto la fiamma si spezza in lingue
                        float n = Fbm(x * 2.6f + 3.3f, yn, zn, 256, 3, 2, 5) * 0.5f + 0.5f;
                        float d = shape * (0.15f + 1.35f * n) - v * 0.62f;
                        d = Mathf.Clamp01(d * 1.35f);
                        // bordo del fotogramma sempre vuoto (niente sbavature tra i fotogrammi nelle mip)
                        float edge = Smooth(0f, 0.06f, u) * Smooth(1f, 0.94f, u) * Smooth(1f, 0.95f, v);
                        d *= edge;
                        float temp = Mathf.Clamp01(Mathf.Pow(d, 0.85f) * (1.18f - v * 0.85f));
                        Color c = FireRamp(temp);
                        px.data[(oy + j) * W + ox + i] = C(c.r, c.g, c.b, Mathf.Pow(d, 1.15f));
                    }
                }
            }
            return px;
        }

        /// <summary>Colore del fuoco per temperatura 0..1 (sRGB): brace rossa scura, rosso, arancio, giallo, bianco caldo.</summary>
        static Color FireRamp(float t)
        {
            Color a = new Color(0.30f, 0.035f, 0.0f), b = new Color(0.86f, 0.17f, 0.02f), c = new Color(1f, 0.47f, 0.07f),
                  d = new Color(1f, 0.78f, 0.30f), e = new Color(1f, 0.97f, 0.84f);
            if (t < 0.25f) return Color.Lerp(a, b, t / 0.25f);
            if (t < 0.5f) return Color.Lerp(b, c, (t - 0.25f) / 0.25f);
            if (t < 0.78f) return Color.Lerp(c, d, (t - 0.5f) / 0.28f);
            return Color.Lerp(d, e, (t - 0.78f) / 0.22f);
        }

        // ------------------------------------------------------------------ fumo
        /// <summary>Lo sbuffo di fumo: una nuvola irregolare (un rumore largo deforma il bordo, uno fine fa i riccioli) che ribolle in
        /// ciclo, illuminata da sopra e un po' da sinistra con la normale presa dalla densita' (la nuvola e' piu' alta dove e' densa),
        /// piu' scura dove e' fitta. Bianco: il colore lo da' la particella.</summary>
        static Px Smoke()
        {
            const int F = 128, N = SheetCols, W = F * N;
            var px = new Px { w = W, h = W, data = new Color32[W * W], mips = true };
            int frames = N * N;
            var den = new float[F * F];
            Vector3 L = new Vector3(-0.35f, 0.75f, 0.55f).normalized;
            const int e = 4;                         // passo delle differenze (pixel): 0,06 nello spazio -1..1
            for (int k = 0; k < frames; k++)
            {
                float tau = (float)k / frames;
                int ox = (k % N) * F, oy = (N - 1 - k / N) * F;
                for (int j = 0; j < F; j++)
                    for (int i = 0; i < F; i++)
                        den[j * F + i] = SmokeDensity((i + 0.5f) / F * 2f - 1f, (j + 0.5f) / F * 2f - 1f, tau);
                for (int j = 0; j < F; j++)
                {
                    float y = (j + 0.5f) / F * 2f - 1f;
                    for (int i = 0; i < F; i++)
                    {
                        float d = den[j * F + i];
                        if (d <= 0.002f) { px.data[(oy + j) * W + ox + i] = new Color32(255, 255, 255, 0); continue; }
                        float dx = den[j * F + Mathf.Min(F - 1, i + e)] - den[j * F + Mathf.Max(0, i - e)];
                        float dy = den[Mathf.Min(F - 1, j + e) * F + i] - den[Mathf.Max(0, j - e) * F + i];
                        var nrm = new Vector3(-dx * 4f, -dy * 4f, 1f).normalized;
                        float lit = Mathf.Clamp01(Vector3.Dot(nrm, L));
                        float g = 0.30f + 0.62f * lit - 0.12f * d + 0.06f * y;
                        px.data[(oy + j) * W + ox + i] = C(g, g, g * 0.98f, Mathf.Pow(Mathf.Clamp01(d * 1.15f), 1.05f));
                    }
                }
            }
            return px;
        }

        static float SmokeDensity(float x, float y, float tau)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > 1f) return 0f;
            float zn = tau * 2f;
            float n = Fbm(x * 1.7f + 3.1f, y * 1.7f - tau * 4f, zn, 256, 4, 2, 5) * 0.5f + 0.5f;
            float m = Fbm(x * 0.9f + 11f, y * 0.9f - tau * 4f, zn, 256, 4, 2, 2) * 0.5f + 0.5f;
            float rr = r + (m - 0.5f) * 0.55f;
            float body = Mathf.Pow(Mathf.Clamp01(1f - rr * rr * 1.15f), 1.6f);
            float d = body * (0.30f + 1.10f * n);
            return Mathf.Clamp01(d - 0.18f) * Smooth(1f, 0.9f, r);
        }

        // ------------------------------------------------------------------ anello, scheggia, goccia
        static Px Ring()
        {
            return Simple(256, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float ang = Mathf.Atan2(y, x);
                float n = Noise(Mathf.Cos(ang) * 2.2f + 9f, Mathf.Sin(ang) * 2.2f + 4f, 0.5f, 256, 256, 256) * 0.5f + 0.5f;
                float band = Mathf.Exp(-Mathf.Pow((r - 0.80f) / (0.055f + 0.03f * n), 2f));
                float inner = 0.22f * Smooth(0.25f, 0.80f, r) * (r < 0.80f ? 1f : 0f);
                float a = Mathf.Clamp01((band * (0.65f + 0.45f * n) + inner) * Smooth(1f, 0.96f, r));
                return new Color(1f, 1f, 1f, a);
            });
        }

        /// <summary>Scheggia di ghiaccio: un rombo lungo e stretto con due facce (una in luce) e lo spigolo chiaro.</summary>
        static Px Shard()
        {
            return Simple(64, (x, y) =>
            {
                float d = Mathf.Abs(x) / 0.30f + Mathf.Abs(y) / 0.96f;
                if (d > 1f) return new Color(1f, 1f, 1f, 0f);
                float face = x < 0f ? 1f : 0.66f;
                float rim = Smooth(0.72f, 1f, d);
                float ridge = Mathf.Exp(-x * x / 0.002f) * 0.6f;
                float g = Mathf.Clamp01(face * 0.75f + rim * 0.5f + ridge);
                return new Color(g, g, g, Mathf.Clamp01((1f - d) * 9f) * (0.55f + 0.45f * Mathf.Max(rim, ridge)));
            });
        }

        /// <summary>Goccia: un disco con l'orlo scuro e un riflesso in alto a sinistra (sangue, icore).</summary>
        static Px Drop()
        {
            return Simple(64, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y * 0.85f);
                if (r > 1f) return new Color(1f, 1f, 1f, 0f);
                float shade = 0.62f - 0.30f * Smooth(0.45f, 1f, r);
                float hx = x + 0.32f, hy = y - 0.36f;
                float spec = Mathf.Exp(-(hx * hx + hy * hy) / 0.025f);
                float g = Mathf.Clamp01(shade + spec * 0.9f);
                return new Color(g, g, g, Mathf.Clamp01((1f - r) * 8f));
            });
        }

        // ------------------------------------------------------------------ rune e cerchio
        /// <summary>Un tratto di runa: segmento, arco o cerchietto, nello spazio del glifo (-1..1).</summary>
        struct Stroke { public int kind; public Vector2 a, b; public float r, a0, a1; }

        static Stroke[][] glyphs;

        /// <summary>I sedici glifi: una spina o un arco portante e due-quattro tratti attaccati ai suoi punti (segmenti tra i nodi di
        /// una griglia 3 x 5, archi, un cerchietto), deterministici.</summary>
        static Stroke[][] Glyphs()
        {
            if (glyphs != null) return glyphs;
            var g = new Stroke[16][];
            var rnd = new System.Random(1666);
            Vector2 Node(int c, int r) => new Vector2((c - 1) * 0.42f, (r - 2) * 0.36f);
            for (int k = 0; k < 16; k++)
            {
                var l = new System.Collections.Generic.List<Stroke>();
                if (k % 3 != 2) l.Add(new Stroke { kind = 0, a = Node(1, 0), b = Node(1, 4) });
                else l.Add(new Stroke { kind = 1, a = Vector2.zero, r = 0.62f, a0 = 200f, a1 = 520f });
                int extra = 2 + rnd.Next(3);
                for (int e = 0; e < extra; e++)
                {
                    int t = rnd.Next(10);
                    if (t < 6)
                    {
                        Vector2 a = Node(rnd.Next(3), rnd.Next(5)), b = Node(rnd.Next(3), rnd.Next(5));
                        if ((a - b).sqrMagnitude < 0.05f) b = Node(2 - rnd.Next(2), 4 - rnd.Next(2));
                        l.Add(new Stroke { kind = 0, a = a, b = b });
                    }
                    else if (t < 9)
                    {
                        float a0 = rnd.Next(8) * 45f;
                        l.Add(new Stroke { kind = 1, a = Node(rnd.Next(3), 1 + rnd.Next(3)), r = 0.25f + 0.1f * rnd.Next(3), a0 = a0, a1 = a0 + 120f + 60f * rnd.Next(3) });
                    }
                    else l.Add(new Stroke { kind = 2, a = Node(rnd.Next(3), rnd.Next(5)), r = 0.12f });
                }
                g[k] = l.ToArray();
            }
            return glyphs = g;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Distanza dal glifo k (spazio del glifo).</summary>
        static float GlyphDist(int k, Vector2 p)
        {
            float d = 9f;
            foreach (var s in Glyphs()[k & 15])
            {
                float e;
                if (s.kind == 0) e = SegDist(p, s.a, s.b);
                else if (s.kind == 2) e = Mathf.Abs((p - s.a).magnitude - s.r);
                else
                {
                    Vector2 q = p - s.a;
                    float ang = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;
                    float mid = (s.a0 + s.a1) * 0.5f, half = (s.a1 - s.a0) * 0.5f;
                    float da = Mathf.Abs(Mathf.DeltaAngle(ang, mid));
                    if (da <= half) e = Mathf.Abs(q.magnitude - s.r);
                    else
                    {
                        float ea = (mid + half) * Mathf.Deg2Rad, sa = (mid - half) * Mathf.Deg2Rad;
                        Vector2 p0 = s.a + new Vector2(Mathf.Cos(sa), Mathf.Sin(sa)) * s.r, p1 = s.a + new Vector2(Mathf.Cos(ea), Mathf.Sin(ea)) * s.r;
                        e = Mathf.Min((p - p0).magnitude, (p - p1).magnitude);
                    }
                }
                d = Mathf.Min(d, e);
            }
            return d;
        }

        /// <summary>Tratto inciso con l'alone: copertura per una distanza d e una larghezza w.</summary>
        static float Ink(float d, float w, float halo) => Mathf.Clamp01(Smooth(w + 0.010f, w - 0.003f, d) + halo * Mathf.Exp(-(d * d) / 0.0011f));

        static Px Runes()
        {
            const int F = 128, N = RuneCols, W = F * N;
            var px = new Px { w = W, h = W, data = new Color32[W * W], mips = true };
            for (int k = 0; k < N * N; k++)
            {
                int ox = (k % N) * F, oy = (N - 1 - k / N) * F;
                for (int j = 0; j < F; j++)
                    for (int i = 0; i < F; i++)
                    {
                        var p = new Vector2((i + 0.5f) / F * 2f - 1f, (j + 0.5f) / F * 2f - 1f) * 1.18f;
                        float d = GlyphDist(k, p);
                        float a = Mathf.Clamp01(Smooth(0.075f, 0.045f, d) + 0.38f * Mathf.Exp(-d * d / 0.018f));
                        a *= Smooth(1.18f, 1.05f, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)));
                        px.data[(oy + j) * W + ox + i] = C(1f, 1f, 1f, a);
                    }
            }
            return px;
        }

        /// <summary>Il cerchio del Dodecaedro (come sulle pagine dei grimori): due anelli esterni con le tacche, la fascia dei dodici
        /// glifi, la stella a dodici punte {12/5}, il dodecagono interno e al centro l'occhio a mandorla con la pupilla.</summary>
        static Px Circle()
        {
            const int S = 512;
            var px = new Px { w = S, h = S, data = new Color32[S * S], mips = true };
            var star = new Vector2[12];
            for (int i = 0; i < 12; i++) { float a = (90f + i * 30f) * Mathf.Deg2Rad; star[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.60f; }
            var dodec = new Vector2[12];
            for (int i = 0; i < 12; i++) { float a = (105f + i * 30f) * Mathf.Deg2Rad; dodec[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.33f; }
            for (int j = 0; j < S; j++)
                for (int i = 0; i < S; i++)
                {
                    var p = new Vector2((i + 0.5f) / S * 2f - 1f, (j + 0.5f) / S * 2f - 1f);
                    float r = p.magnitude;
                    if (r > 1f) { px.data[j * S + i] = new Color32(255, 255, 255, 0); continue; }
                    float ang = Mathf.Atan2(p.y, p.x);
                    float a = 0f;
                    a = Mathf.Max(a, Ink(Mathf.Abs(r - 0.955f), 0.010f, 0.5f));
                    a = Mathf.Max(a, Ink(Mathf.Abs(r - 0.905f), 0.004f, 0.3f));
                    // tacche ogni 6 gradi tra i due anelli
                    float tick = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, Mathf.Round(ang * Mathf.Rad2Deg / 6f) * 6f)) * Mathf.Deg2Rad * r;
                    if (r > 0.91f && r < 0.95f) a = Mathf.Max(a, Ink(tick, 0.003f, 0.2f));
                    // la fascia dei glifi
                    if (r > 0.66f && r < 0.89f)
                    {
                        float deg = ang * Mathf.Rad2Deg;
                        int sector = Mathf.RoundToInt(deg / 30f);
                        float delta = Mathf.DeltaAngle(sector * 30f, deg) * Mathf.Deg2Rad;
                        var gp = new Vector2(delta * r / 0.10f, (r - 0.775f) / 0.10f);
                        float gd = GlyphDist(((sector % 12) + 12) % 12, gp) * 0.10f;
                        a = Mathf.Max(a, Ink(gd, 0.0065f, 0.35f));
                    }
                    a = Mathf.Max(a, Ink(Mathf.Abs(r - 0.645f), 0.006f, 0.4f));
                    a = Mathf.Max(a, Ink(Mathf.Abs(r - 0.615f), 0.0025f, 0.2f));
                    // stella {12/5} e dodecagono
                    float ds = 9f, dd = 9f;
                    for (int s = 0; s < 12; s++)
                    {
                        ds = Mathf.Min(ds, SegDist(p, star[s], star[(s + 5) % 12]));
                        dd = Mathf.Min(dd, SegDist(p, dodec[s], dodec[(s + 1) % 12]));
                    }
                    a = Mathf.Max(a, Ink(ds, 0.0035f, 0.3f) * 0.85f);
                    a = Mathf.Max(a, Ink(dd, 0.005f, 0.35f));
                    // l'occhio: mandorla (due archi di cerchio per gli angoli a +-0,24 e le palpebre a +-0,11) e pupilla
                    const float eyeE = 0.24f, eyeH = 0.11f;
                    const float eyeR = (eyeE * eyeE + eyeH * eyeH) / (2f * eyeH), eyeC = eyeR - eyeH;
                    var q = new Vector2(p.x, Mathf.Abs(p.y));
                    float lid = Mathf.Abs(p.x) <= eyeE ? Mathf.Abs((q - new Vector2(0f, -eyeC)).magnitude - eyeR)
                                                       : (q - new Vector2(eyeE, 0f)).magnitude;
                    a = Mathf.Max(a, Ink(lid, 0.005f, 0.4f));
                    float pr = p.magnitude;
                    a = Mathf.Max(a, Ink(Mathf.Abs(pr - 0.085f), 0.006f, 0.4f));
                    a = Mathf.Max(a, Smooth(0.045f, 0.035f, pr));
                    a *= Smooth(1f, 0.975f, r);
                    px.data[j * S + i] = C(1f, 1f, 1f, a);
                }
            return px;
        }

        // ------------------------------------------------------------------ fulmini e nastri
        /// <summary>Profilo di un fulmine o di una scia (u lungo, v di traverso): cuore stretto e alone largo.</summary>
        static Px Beam()
        {
            const int Wd = 64, H = 32;
            var px = new Px { w = Wd, h = H, data = new Color32[Wd * H], mips = false };
            for (int j = 0; j < H; j++)
                for (int i = 0; i < Wd; i++)
                {
                    float v = (j + 0.5f) / H * 2f - 1f;
                    float a = Mathf.Exp(-v * v / 0.035f) + 0.35f * Mathf.Exp(-v * v / 0.25f);
                    px.data[j * Wd + i] = C(1f, 1f, 1f, Mathf.Clamp01(a) * Smooth(1f, 0.85f, Mathf.Abs(v)));
                }
            return px;
        }

        /// <summary>Il nastro del sangue che corre (si ripete lungo u): un rivolo dai bordi frastagliati con le strisce lucide.</summary>
        static Px Ribbon()
        {
            const int Wd = 128, H = 32;
            var px = new Px { w = Wd, h = H, data = new Color32[Wd * H], mips = true, wrap = TextureWrapMode.Repeat };
            for (int j = 0; j < H; j++)
                for (int i = 0; i < Wd; i++)
                {
                    float u = (float)i / Wd, v = (j + 0.5f) / H * 2f - 1f;
                    float edge = 0.62f + 0.30f * Noise(u * 8f, 0.5f, 1.5f, 8, 256, 256);
                    float a = Smooth(edge, edge - 0.25f, Mathf.Abs(v));
                    float streak = Noise(u * 4f, v * 3f + 7f, 2.5f, 4, 256, 256) * 0.5f + 0.5f;
                    float hi = Mathf.Exp(-Mathf.Pow((v + 0.25f) / 0.12f, 2f)) * (0.5f + 0.5f * streak);
                    float g = Mathf.Clamp01(0.45f + 0.25f * streak + hi * 0.8f);
                    px.data[j * Wd + i] = C(g, g, g, a);
                }
            return px;
        }
    }
}
