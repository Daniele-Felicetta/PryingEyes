using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>Texture e materiali del giardino segreto, fatti a mano a runtime (il kit non ha erba ne' foglie): erba, siepe, foglie con alfa
    /// per le chiome, aiuole, cielo stellato con la luna. Una copia per sessione.</summary>
    static class GardenKit
    {
        static Texture2D grass, leaf, hedge, flowers, sky;
        static Material mGrass, mLeaf, mHedge, mBed, mSky, mBark;

        internal static float H(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Rumore a valori, periodico (la texture si ripete senza giunture).</summary>
        internal static float VN(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int x0 = ((xi % period) + period) % period, x1 = (x0 + 1) % period, y0 = ((yi % period) + period) % period, y1 = (y0 + 1) % period;
            return Mathf.Lerp(Mathf.Lerp(H(x0, y0, seed), H(x1, y0, seed), fx), Mathf.Lerp(H(x0, y1, seed), H(x1, y1, seed), fx), fy);
        }

        internal static float Fbm(float u, float v, int baseP, int seed)
        {
            float sum = 0f, amp = 0.55f, norm = 0f;
            int p = baseP;
            for (int o = 0; o < 3; o++)
            {
                sum += amp * VN(u * p, v * p, p, seed + o * 17);
                norm += amp; amp *= 0.5f; p *= 2;
            }
            return sum / norm;
        }

        internal static Texture2D Make(string name, int w, int h, Func<int, int, Color> px, bool mips = true)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mips) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            var c = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) c[y * w + x] = px(x, y);
            t.SetPixels32(c);
            t.Apply(mips, false);
            return t;
        }

        /// <summary>Disegna foglie ellittiche in un buffer: ogni foglia ha centro, semiassi, angolo e colore.</summary>
        static void Leaves(Color32[] buf, int w, int h, int count, int seed, Color dark, Color light, bool opaqueBackground)
        {
            var rng = new Rng(seed);
            for (int i = 0; i < count; i++)
            {
                float cx = rng.Range(0f, w), cy = rng.Range(0f, h), ang = rng.Range(0f, Mathf.PI);
                float la = rng.Range(w * 0.045f, w * 0.085f), lb = la * rng.Range(0.38f, 0.55f);
                Color col = Color.Lerp(dark, light, rng.Range(0f, 1f));
                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                int r = Mathf.CeilToInt(la) + 1;
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        float u = dx * ca + dy * sa, v = -dx * sa + dy * ca;
                        if ((u * u) / (la * la) + (v * v) / (lb * lb) > 1f) continue;
                        int px = (int)((cx + dx + w * 4) % w), py = (int)((cy + dy + h * 4) % h);
                        float vein = Mathf.Abs(v) < 0.6f ? 1.25f : 1f;                  // la nervatura centrale e' piu' chiara
                        var c = col * vein; c.a = 1f;
                        buf[py * w + px] = c;
                    }
            }
        }

        internal static Material LitMat(string name, Texture2D tex, float tile, float smooth)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", new Vector2(tile, tile));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            return m;
        }

        public static Material Grass()
        {
            if (mGrass != null) return mGrass;
            grass = Make("giardino_erba", 128, 128, (x, y) =>
            {
                float u = x / 128f, v = y / 128f;
                float n = Fbm(u, v, 4, 11), fine = H(x, y, 5);
                var a = new Color(0.050f, 0.125f, 0.040f); var b = new Color(0.150f, 0.285f, 0.085f);
                var c = Color.Lerp(a, b, Mathf.Clamp01(n * 1.25f - 0.12f)) * (0.82f + fine * 0.36f);
                if (fine > 0.93f) c = Color.Lerp(c, new Color(0.28f, 0.42f, 0.14f), 0.7f);        // fili d'erba chiari
                c.a = 1f;
                return c;
            });
            mGrass = LitMat("giardino_erba", grass, 0.34f, 0.04f);
            return mGrass;
        }

        public static Material Leaf()
        {
            if (mLeaf != null) return mLeaf;
            const int S = 128;
            var buf = new Color32[S * S];
            Leaves(buf, S, S, 150, 31, new Color(0.04f, 0.14f, 0.04f), new Color(0.20f, 0.42f, 0.10f), false);
            leaf = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "giardino_foglie", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            leaf.SetPixels32(buf); leaf.Apply(true, false);
            mLeaf = LitMat("giardino_foglie", leaf, 1f, 0.12f);
            mLeaf.SetFloat("_Cull", 0f);
            mLeaf.SetFloat("_AlphaClip", 1f);
            mLeaf.SetFloat("_Cutoff", 0.5f);
            mLeaf.EnableKeyword("_ALPHATEST_ON");
            mLeaf.SetOverrideTag("RenderType", "TransparentCutout");
            mLeaf.renderQueue = (int)RenderQueue.AlphaTest;
            return mLeaf;
        }

        public static Material Hedge()
        {
            if (mHedge != null) return mHedge;
            const int S = 256;
            var buf = new Color32[S * S];
            for (int i = 0; i < buf.Length; i++) buf[i] = new Color(0.025f, 0.075f, 0.025f);
            Leaves(buf, S, S, 700, 41, new Color(0.04f, 0.13f, 0.04f), new Color(0.17f, 0.38f, 0.09f), true);
            hedge = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "giardino_siepe", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            hedge.SetPixels32(buf); hedge.Apply(true, false);
            mHedge = LitMat("giardino_siepe", hedge, 0.5f, 0.08f);
            return mHedge;
        }

        public static Material Bed()
        {
            if (mBed != null) return mBed;
            // fogliame scuro con fiori a macchie tonde dal bordo morbido (una per cella, colore a zone): niente coriandoli
            const int S = 128, cell = 16, cells = S / cell;
            var palette = new[] { new Color(0.50f, 0.06f, 0.09f), new Color(0.40f, 0.33f, 0.62f), new Color(0.66f, 0.50f, 0.13f), new Color(0.62f, 0.58f, 0.52f) };
            flowers = Make("giardino_aiuola", S, S, (x, y) =>
            {
                float n = Fbm(x / (float)S, y / (float)S, 5, 7);
                var c = Color.Lerp(new Color(0.055f, 0.105f, 0.038f), new Color(0.115f, 0.205f, 0.068f), n);
                int cx0 = x / cell, cy0 = y / cell;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int gx = ((cx0 + ox) % cells + cells) % cells, gy = ((cy0 + oy) % cells + cells) % cells;
                        if (H(gx, gy, 9) < 0.14f) continue;
                        float px = (cx0 + ox) * cell + cell * (0.28f + 0.44f * H(gx, gy, 3));
                        float py = (cy0 + oy) * cell + cell * (0.28f + 0.44f * H(gx, gy, 4));
                        float r = cell * (0.36f + 0.20f * H(gx, gy, 5));
                        float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                        if (d >= r) continue;
                        float t = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - r * 0.45f) / (r * 0.55f)));
                        int k = Mathf.Clamp((int)(VN(gx * 0.5f, gy * 0.5f, cells / 2, 21) * 4f), 0, 3);
                        var flower = palette[k] * (0.85f + 0.3f * (1f - d / r));
                        if (d < r * 0.22f) flower = new Color(0.55f, 0.42f, 0.10f);               // il cuore del fiore
                        c = Color.Lerp(c, flower, t);
                    }
                c.a = 1f;
                return c;
            });
            mBed = LitMat("giardino_aiuola", flowers, 0.6f, 0.05f);
            return mBed;
        }

        public static Material Bark()
        {
            if (mBark != null) return mBark;
            mBark = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "giardino_corteccia", color = new Color(0.075f, 0.052f, 0.035f) };
            mBark.SetFloat("_Smoothness", 0.08f);
            return mBark;
        }

        /// <summary>Cielo notturno con stelle e luna, su una mezza sfera attorno al giardino. Unlit, dietro a tutto.</summary>
        public static Material Sky()
        {
            if (mSky != null) return mSky;
            const int W = 512, Hh = 256;
            sky = Make("giardino_cielo", W, Hh, (x, y) =>
            {
                float u = x / (float)W, v = y / (float)Hh;                       // v: 0 orizzonte, 1 zenit
                var c = Color.Lerp(new Color(0.045f, 0.060f, 0.115f), new Color(0.006f, 0.012f, 0.045f), Mathf.Pow(v, 0.6f));
                float s = H(x, y, 77);
                if (s > 0.9965f) { float k = 0.5f + 0.5f * H(x, y, 78); c = Color.Lerp(c, Color.white, k); }
                else if (s > 0.992f) c = Color.Lerp(c, new Color(0.8f, 0.85f, 1f), 0.35f);
                float dx = Mathf.Min(Mathf.Abs(u - 0.62f), 1f - Mathf.Abs(u - 0.62f)) * W, dy = (v - 0.66f) * Hh;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < 9f) c = new Color(1f, 0.98f, 0.9f);
                else if (d < 26f) c = Color.Lerp(c, new Color(0.45f, 0.55f, 0.85f), Mathf.Pow(1f - (d - 9f) / 17f, 2f) * 0.55f);
                c.a = 1f;
                return c;
            }, false);
            sky.wrapMode = TextureWrapMode.Repeat;
            mSky = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "giardino_cielo" };
            mSky.SetTexture("_BaseMap", sky);
            mSky.SetColor("_BaseColor", new Color(2.4f, 2.4f, 2.6f, 1f));
            mSky.SetFloat("_Cull", 0f);
            mSky.SetFloat("_ZWrite", 0f);
            mSky.renderQueue = (int)RenderQueue.Background;
            return mSky;
        }

        /// <summary>La mezza sfera del cielo: anelli di elevazione da -8 a 90 gradi, facce verso l'interno.</summary>
        public static Mesh SkyDome(float radius)
        {
            const int rings = 14, slices = 40;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float el = Mathf.Lerp(-8f, 90f, i / (float)rings) * Mathf.Deg2Rad;
                for (int j = 0; j <= slices; j++)
                {
                    float az = j * Mathf.PI * 2f / slices;
                    v.Add(new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el)) * radius);
                    uv.Add(new Vector2(j / (float)slices, Mathf.Clamp01((el * Mathf.Rad2Deg + 8f) / 98f)));
                }
            }
            int row = slices + 1;
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < slices; j++)
                {
                    int a = i * row + j, b = a + 1, c = a + row, d = c + 1;
                    t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c);
                }
            var m = new Mesh { name = "giardino_cupola" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    public static partial class FloorBuilder
    {
        /// <summary>Costruisce i giardini segreti (fuori dai muri delle sale madri). Chiamato dopo tutte le stanze.</summary>
        static void BuildGardens(FloorInstance fi)
        {
            foreach (var a in fi.plan.annexes)
                if (a.kind == AnnexKind.Giardino) BuildGarden(fi, a);
        }

        /// <summary>Le aperture del giardino nel muro di una sala (b = -2), per le facce di quel lato.</summary>
        static IEnumerable<Opening> AnnexDoorsOn(Plan plan, Room r, Side s)
        {
            bool ax = Plan.AlongX(s);
            int line = Plan.Line(r, s);
            foreach (var o in plan.annexDoors)
                if (o.a == r.id && o.alongX == ax && o.line == line) yield return o;
        }

        static void BuildGarden(FloorInstance fi, Annex a)
        {
            var site = NewSite(fi, a);
            var tr = site.transform;
            var rng = new Rng(a.seed);
            Material stone = Mat("mur_pietra"), path = Mat("pav_lastre"), trim = Mat("cornice_pietra"), oak = Mat("quercia_portale"), iron = Mat("ferro");
            Material grass = GardenKit.Grass(), leaf = GardenKit.Leaf(), hedge = GardenKit.Hedge(), bed = GardenKit.Bed(), bark = GardenKit.Bark();

            // il riferimento del giardino: 't' si allontana dal muro della sala, 's' corre lungo il muro dal centro della porta
            Vector3 outV = a.side == Side.N ? Vector3.forward : a.side == Side.S ? Vector3.back : a.side == Side.E ? Vector3.right : Vector3.left;
            bool alongX = Plan.AlongX(a.side);
            Vector3 al = alongX ? Vector3.right : Vector3.forward;
            Vector3 origin = alongX ? new Vector3(M(a.center), 0f, M(a.line)) : new Vector3(M(a.line), 0f, M(a.center));
            Vector3 G(float t, float s, float y = 0f) => origin + outV * t + al * s + Vector3.up * y;
            Vector3 Sz(float tLen, float h, float sLen) => alongX ? new Vector3(sLen, h, tLen) : new Vector3(tLen, h, sLen);
            int sign = (a.side == Side.N || a.side == Side.E) ? 1 : -1;
            int n0 = alongX ? a.z0 : a.x0, n1 = alongX ? a.z1 : a.x1;
            float tA = sign > 0 ? M(n0 - a.line) : M(a.line - n1);
            float tB = sign > 0 ? M(n1 - a.line) : M(a.line - n0);
            float sLo = M((alongX ? a.x0 : a.z0) - a.center), sHi = M((alongX ? a.x1 : a.z1) - a.center);
            const float WallH = 6.5f, wt = 0.3f, halfP = PlanAnnexGen.PassageW * 0.05f;
            float tI0 = tA + wt, tI1 = tB - wt, sI0 = sLo + wt, sI1 = sHi - wt;
            float sF = (sI0 + sI1) * 0.5f, tF = Mathf.Lerp(tI0, tI1, 0.46f);
            Vector3 fountain = G(tF, sF);

            // ---- il terreno: passaggio in lastre, giardino a prato
            var ground = new MeshBuilder();
            ground.QuadW(path, G(0f, -halfP), G(tA + wt, -halfP), G(tA + wt, halfP), G(0f, halfP), Vector3.up);
            ground.QuadW(grass, G(tI0, sI0), G(tI1, sI0), G(tI1, sI1), G(tI0, sI1), Vector3.up);
            Vector3 pA = G(tI0, 0f, 0.012f), pB = G(tF - 2.2f, sF, 0.012f);
            Vector3 pd = (pB - pA).normalized, pp = new Vector3(-pd.z, 0f, pd.x);
            ground.QuadW(path, pA - pp * 0.85f, pB - pp * 0.85f, pB + pp * 0.85f, pA + pp * 0.85f, Vector3.up);
            for (int i = 0; i < 28; i++)
                RingCap(ground, path, fountain, 0f, 2.6f, i * 360f / 28f, (i + 1) * 360f / 28f, 0.014f, true);
            FlushObj(tr, "Terreno visibile", ground, false);
            var terrain = new GameObject("Terreno");
            terrain.transform.SetParent(tr, false);
            var tb = terrain.AddComponent<BoxCollider>();
            tb.center = G((tI0 + tI1) * 0.5f, (sI0 + sI1) * 0.5f, -0.25f); tb.size = Sz(tI1 - tI0, 0.5f, sI1 - sI0);
            var pb = terrain.AddComponent<BoxCollider>();
            pb.center = G((tA + wt) * 0.5f, 0f, -0.25f); pb.size = Sz(tA + wt, 0.5f, halfP * 2f);

            // ---- il passaggio coperto e i muri del giardino
            var walls = new MeshBuilder();
            foreach (float sg in new[] { -1f, 1f })
                walls.Box(stone, G((0.15f + tA + wt) * 0.5f, sg * (halfP + wt * 0.5f), 1.65f), Sz(tA + wt - 0.15f, 3.3f, wt));
            walls.Box(stone, G((0.15f + tA + wt) * 0.5f, 0f, 3.15f), Sz(tA + wt - 0.15f, 0.3f, halfP * 2f + wt * 2f));
            // giardino: due fianchi, il fondo e il muro vicino col varco del passaggio
            foreach (float sg in new[] { sLo + wt * 0.5f, sHi - wt * 0.5f })
                walls.Box(stone, G((tA + tB) * 0.5f, sg, WallH * 0.5f), Sz(tB - tA, WallH, wt));
            walls.Box(stone, G(tB - wt * 0.5f, (sI0 + sI1) * 0.5f, WallH * 0.5f), Sz(wt, WallH, sI1 - sI0));
            walls.Box(stone, G(tA + wt * 0.5f, (sI0 - halfP) * 0.5f - halfP * 0.5f + sI0 * 0.0f, WallH * 0.5f), Sz(wt, WallH, Mathf.Max(0.05f, -halfP - sI0)));
            walls.Box(stone, G(tA + wt * 0.5f, (halfP + sI1) * 0.5f, WallH * 0.5f), Sz(wt, WallH, Mathf.Max(0.05f, sI1 - halfP)));
            walls.Box(stone, G(tA + wt * 0.5f, 0f, (3.1f + WallH) * 0.5f), Sz(wt, WallH - 3.1f, halfP * 2f));
            // cimase
            walls.Box(trim, G((tA + tB) * 0.5f, sLo + wt * 0.5f, WallH + 0.1f), Sz(tB - tA, 0.2f, 0.55f));
            walls.Box(trim, G((tA + tB) * 0.5f, sHi - wt * 0.5f, WallH + 0.1f), Sz(tB - tA, 0.2f, 0.55f));
            walls.Box(trim, G(tB - wt * 0.5f, (sI0 + sI1) * 0.5f, WallH + 0.1f), Sz(0.55f, 0.2f, sI1 - sI0));
            FlushObj(tr, "Muri del giardino", walls, true);

            // ---- vegetazione e arredi
            var propsGo = site.props;
            var barkMb = new MeshBuilder(); var leafMb = new MeshBuilder(); var hedgeMb = new MeshBuilder(); var bedMb = new MeshBuilder();
            Vector3 chest = G(tI1 - 2.4f, sF);
            var taken = new List<Vector3> { fountain, chest };
            int trees = 0;
            for (int i = 0; i < 140 && trees < 6; i++)
            {
                float t = rng.Range(tI0 + 1.5f, tI1 - 1.5f), s = rng.Range(sI0 + 1.5f, sI1 - 1.5f);
                Vector3 p = G(t, s);
                if (DistSeg(p, pA, pB) < 2.2f || (p - fountain).magnitude < 4.2f || (p - chest).magnitude < 3.2f) continue;
                bool near = false;
                foreach (var q in taken) if ((q - p).magnitude < 3.4f) near = true;
                if (near) continue;
                taken.Add(p); trees++;
                BuildTree(barkMb, leafMb, p, rng.Range(3.8f, 5.4f), rng.Range(0.20f, 0.30f), rng);
                var col = new GameObject("Tronco"); col.transform.SetParent(propsGo, false);
                var bc = col.AddComponent<BoxCollider>(); bc.center = p + Vector3.up * 1.5f; bc.size = new Vector3(0.55f, 3f, 0.55f);
            }
            // due siepi lunghe lungo i fianchi, con un varco ciascuna
            foreach (float sg in new[] { sI0 + 1.9f, sI1 - 1.9f })
            {
                float tStart = tI0 + 3.2f, tEnd = tI1 - 3.4f, gap = Mathf.Lerp(tStart, tEnd, rng.Range(0.35f, 0.65f));
                hedgeMb.Box(hedge, G((tStart + gap - 0.9f) * 0.5f, sg, 0.72f), Sz(gap - 0.9f - tStart, 1.44f, 0.85f));
                hedgeMb.Box(hedge, G((gap + 0.9f + tEnd) * 0.5f, sg, 0.72f), Sz(tEnd - gap - 0.9f, 1.44f, 0.85f));
            }
            // aiuole ai lati del sentiero, vicino al varco
            foreach (float sg in new[] { -3.0f, 3.0f })
                bedMb.Box(bed, G(tI0 + 2.2f, sg, 0.1f), Sz(3.6f, 0.2f, 1.0f));
            FlushObj(propsGo, "Tronchi", barkMb, false);
            FlushObj(propsGo, "Chiome", leafMb, false);
            FlushObj(propsGo, "Siepi", hedgeMb, true);
            FlushObj(propsGo, "Aiuole", bedMb, true);
            float faceOut = YawOfDir(-outV);
            AnnexProp(propsGo, "chi_fonte", fountain, 0f, 1.5f);
            foreach (float sg in new[] { -3.7f, 3.7f })
            {
                Vector3 pos = G(tF, sF + sg);
                AnnexProp(propsGo, "chi_panca", pos, YawOfDir(fountain - pos), 1f);
            }
            foreach (float sg in new[] { -3.3f, 3.3f })
                AnnexProp(propsGo, "chi_statua", G(tI1 - 1.0f, sF + sg), faceOut, 1f);
            // lampioni agli angoli e luce di luna dall'alto
            foreach (float tt in new[] { tI0 + 1.2f, tI1 - 1.2f })
                foreach (float ss in new[] { sI0 + 1.2f, sI1 - 1.2f })
                {
                    AnnexProp(propsGo, "chi_candelabro", G(tt, ss), 0f, 1f);
                    ShrineKit.PointLight(tr, "Lume del giardino", G(tt, ss, 1.8f), new Color(1f, 0.64f, 0.32f), 2.8f * LightPool.Dim, 8.5f, 0.2f, 3.0f);
                }
            ShrineKit.PointLight(tr, "Lume del passaggio", G(2.2f, 0f, 2.4f), new Color(1f, 0.64f, 0.32f), 2.2f * LightPool.Dim, 5.5f, 0.2f, 2.6f);
            var moon = new GameObject("Luce di luna");
            moon.transform.SetParent(tr, false);
            moon.transform.position = G((tA + tB) * 0.5f, (sI0 + sI1) * 0.5f, 13f);
            moon.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var ml = moon.AddComponent<Light>();
            ml.type = LightType.Spot; ml.spotAngle = 112f; ml.innerSpotAngle = 70f; ml.range = 32f;
            ml.color = new Color(0.52f, 0.66f, 1f); ml.intensity = 250f;
            LightPool.Register(ml, false, true);
            // il cielo: una mezza sfera attorno al giardino
            var dome = new GameObject("Cielo");
            dome.transform.SetParent(tr, false);
            dome.transform.position = G((tA + tB) * 0.5f, (sI0 + sI1) * 0.5f, 0f);
            dome.AddComponent<MeshFilter>().sharedMesh = GardenKit.SkyDome(26f);
            var dr = dome.AddComponent<MeshRenderer>();
            dr.sharedMaterial = GardenKit.Sky();
            dr.shadowCastingMode = ShadowCastingMode.Off; dr.receiveShadows = false;

            // ---- la porta nel muro della sala, nascosta dall'edera
            var closer = new GameObject("Porta del giardino");
            closer.transform.SetParent(tr, false);
            var dm = new MeshBuilder();
            float dw = a.width * 0.1f, dh = 2.25f;
            // Sz() orienta gia' le misure sull'asse del muro: il box resta allineato agli assi, senza rotazione
            dm.Box(oak, G(0f, 0f, dh * 0.5f), Sz(0.2f, dh, dw));
            foreach (float yy in new[] { 0.55f, 1.65f })
                dm.Box(iron, G(0.1f, 0f, yy), Sz(0.05f, 0.1f, dw));
            var doorGo = FlushObj(closer.transform, "Battente", dm, true, HotelNav.DoorLayer);
            site.closer = closer.transform;
            site.closerCol = doorGo != null ? doorGo.GetComponent<Collider>() : null;
            // l'edera: un tappeto di siepe fitta sul muro, dal lato della sala, con foglie sporgenti ai bordi
            var ivy = new GameObject("Edera");
            ivy.transform.SetParent(closer.transform, false);
            var iv = new MeshBuilder(); var ivLeaf = new MeshBuilder();
            float iw = dw + 1.0f, ih = dh + 0.9f, ti = -FloorBuilder.TH - 0.045f;
            iv.QuadW(hedge, G(ti, -iw * 0.5f), G(ti, iw * 0.5f), G(ti, iw * 0.5f, ih), G(ti, -iw * 0.5f, ih), -outV);
            for (int i = 0; i < 46; i++)
            {
                float s = rng.Range(-iw * 0.5f, iw * 0.5f), y = rng.Range(0.05f, ih);
                float edge = Mathf.Max(Mathf.Abs(s) / (iw * 0.5f), y / ih);
                if (rng.Chance(0.55) && edge < 0.6f) continue;
                Vector3 c = G(ti - rng.Range(0.01f, 0.07f), s, y);
                float sz = rng.Range(0.30f, 0.55f), rot = rng.Range(0f, Mathf.PI * 2f);
                Vector3 ax = al * Mathf.Cos(rot) + Vector3.up * Mathf.Sin(rot), ay = Vector3.Cross(-outV, ax);
                ivLeaf.Quad(leaf, c - ax * sz - ay * sz, c + ax * sz - ay * sz, c + ax * sz + ay * sz, c - ax * sz + ay * sz,
                            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), -outV);
            }
            FlushObj(ivy.transform, "Tappeto", iv, false);
            FlushObj(ivy.transform, "Foglie", ivLeaf, false);
            site.fade.Add(ivy.transform);
            site.sigilMat = ShrineKit.EmitOwn(GateColor(a.gate), 0f);
            site.gatePoint = G(ti - 0.1f, 0f, 1.4f);
            site.gateRadius = 2.8f;
            site.outside = G(-1.4f, 0f);          // davanti al muro, nella sala madre: il lato da cui si arriva a cancello chiuso
            site.inside = G(tI0 + 1.5f, 0f);
            site.rewardPos = chest;
            site.rewardYaw = faceOut;
            site.altarPos = G(-1.1f, 1.9f);       // il tributo (se serve) nella sala: ai piedi dell'edera
            site.altarYaw = YawOfDir(outV);
            site.keyPos = G(tI1 - 2.4f, sF + 2.2f);
            site.cellFloorY = 0f;
        }

        static float DistSeg(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a, ap = p - a;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            Vector3 q = a + ab * t - p;
            q.y = 0f;
            return q.magnitude;
        }

        /// <summary>Un albero: tronco rastremato a otto lati, una piccola diramazione e una chioma di carte di foglie orientate a caso.</summary>
        static void BuildTree(MeshBuilder barkMb, MeshBuilder leafMb, Vector3 root, float h, float r, Rng rng)
        {
            var bark = GardenKit.Bark();
            const int segs = 8;
            float rTop = r * 0.55f;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * 360f / segs, a1 = (i + 1) * 360f / segs;
                Vector3 b0 = Pol(root, r, a0, 0f), b1 = Pol(root, r, a1, 0f);
                Vector3 t0 = Pol(root, rTop, a0, h * 0.62f), t1 = Pol(root, rTop, a1, h * 0.62f);
                barkMb.QuadSmooth(bark, b0, b1, t1, t0, Dir(a0), Dir(a1), Dir(a1), Dir(a0), new Vector2(i * 0.3f, 0f), new Vector2(i * 0.3f + 0.3f, 0f), new Vector2(i * 0.3f + 0.3f, h), new Vector2(i * 0.3f, h));
            }
            Vector3 crown = root + Vector3.up * (h * 0.76f);
            for (int b = 0; b < 3; b++)
            {
                Vector3 d = Dir(rng.Range(0f, 360f)) * rng.Range(0.7f, 1.2f) + Vector3.up * rng.Range(0.7f, 1.1f);
                barkMb.Beam(bark, root + Vector3.up * (h * 0.5f), crown + d, 0.12f, 0.12f, Vector3.up);
            }
            var leaf = GardenKit.Leaf();
            float rad = h * 0.42f;
            for (int i = 0; i < 26; i++)
            {
                Vector3 dir = UnityEngine.Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.8f + 0.05f;
                Vector3 c = crown + new Vector3(dir.x, dir.y * 0.8f, dir.z) * rad * rng.Range(0.35f, 1f);
                float sz = rng.Range(0.75f, 1.15f), rot = rng.Range(0f, Mathf.PI * 2f);
                Vector3 n = new Vector3(rng.Range(-1f, 1f), rng.Range(-0.4f, 1f), rng.Range(-1f, 1f)).normalized;
                Vector3 ax = Vector3.Cross(n, Vector3.up); if (ax.sqrMagnitude < 0.01f) ax = Vector3.right;
                ax = (ax.normalized * Mathf.Cos(rot) + Vector3.Cross(n, ax.normalized) * Mathf.Sin(rot));
                Vector3 ay = Vector3.Cross(n, ax);
                leafMb.Quad(leaf, c - ax * sz - ay * sz, c + ax * sz - ay * sz, c + ax * sz + ay * sz, c - ax * sz + ay * sz,
                            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), n);
            }
        }
    }
}
