using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>The look of the chest (Scrigno.cs): blackened wood, rusted iron and a rotten lining painted at run time (albedo, normal
    /// map and a metallic/occlusion/smoothness mask for each), the meshes the chest is made of (boxes with UVs in metres, a barrel vault,
    /// a dome shell for the eyelids, a torus, a cone, a sphere, a cylinder) and a bag that merges the static pieces into a few meshes.
    /// Everything is procedural and nothing is serialized, so a scripts-only build is enough (see docs/engineering/building-and-shipping.md).
    /// Colours of the textures are written as they look on screen (sRGB).</summary>
    public static class ScrignoArt
    {
        // ------------------------------------------------------------------ palette
        /// <summary>The eye of the lock: a dull blood red. The only warm colour left on the chest.</summary>
        public static readonly Color EyeRed = new Color(1f, 0.11f, 0.04f);
        /// <summary>What comes out of the open chest: a pale, cold light, like moonlight through a crypt grating.</summary>
        public static readonly Color Cold = new Color(0.52f, 0.70f, 1f);

        /// <summary>Size in metres of one repeat of each texture (the UVs of the meshes are in these units, so the grain keeps its scale on any face).</summary>
        public static readonly Vector2 WoodTile = new Vector2(1f, 0.5f), IronTile = new Vector2(0.5f, 0.5f), LiningTile = new Vector2(0.25f, 0.25f);

        public static Material Wood, Iron, Lining, Brass, Bone, CoinA, CoinB, Sclera, Socket;
        static Texture2D woodAlb, woodNor, woodMask, ironAlb, ironNor, ironMask, liningAlb, liningNor, liningMask;

        public static bool Ready => Wood != null;

        // ------------------------------------------------------------------ noise and painting helpers
        static float SS(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        /// <summary>Periodic value noise: 'px' by 'py' cells per tile (at most 64), so a texture painted from it tiles without seams.</summary>
        sealed class Lattice
        {
            readonly float[] t = new float[64 * 64];
            public Lattice(int seed) { var r = new System.Random(seed); for (int i = 0; i < t.Length; i++) t[i] = (float)r.NextDouble(); }
            static int Wrap(int i, int p) { i %= p; return i < 0 ? i + p : i; }
            public float At(float u, float v, int px, int py, int salt)
            {
                float x = u * px, y = v * py;
                int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
                float fx = x - ix, fy = y - iy;
                fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
                int x0 = Wrap(ix, px), x1 = Wrap(ix + 1, px), y0 = (Wrap(iy, py) + salt * 29) & 63, y1 = (Wrap(iy + 1, py) + salt * 29) & 63;
                x0 = (x0 + salt * 13) & 63; x1 = (x1 + salt * 13) & 63;
                float a = t[y0 * 64 + x0], b = t[y0 * 64 + x1], c = t[y1 * 64 + x0], d = t[y1 * 64 + x1];
                return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
            }
            public float Fbm(float u, float v, int px, int py, int oct, int salt = 0)
            {
                float s = 0f, amp = 0.5f, norm = 0f;
                for (int o = 0; o < oct; o++)
                {
                    s += amp * At(u, v, Mathf.Min(px << o, 64), Mathf.Min(py << o, 64), salt + o);
                    norm += amp; amp *= 0.5f;
                }
                return s / norm;
            }
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0x1000000;
            }
        }

        static Texture2D Pack(string name, int w, int h, Color32[] px, bool linear)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, linear) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        /// <summary>Normal map (OpenGL convention, alpha 1 so it reads as plain RGB or as DXT5nm) from a height field that wraps around.</summary>
        static Texture2D NormalFrom(string name, int w, int h, float[] hgt, float strength)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = hgt[y * w + (x + 1) % w] - hgt[y * w + (x + w - 1) % w];
                    float dy = hgt[((y + 1) % h) * w + x] - hgt[((y + h - 1) % h) * w + x];
                    var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    px[y * w + x] = new Color32((byte)(n.x * 127.5f + 127.5f), (byte)(n.y * 127.5f + 127.5f), (byte)(n.z * 127.5f + 127.5f), 255);
                }
            return Pack(name, w, h, px, true);
        }

        static Color32 C(Vector3 c) => new Color32((byte)(Mathf.Clamp01(c.x) * 255f), (byte)(Mathf.Clamp01(c.y) * 255f), (byte)(Mathf.Clamp01(c.z) * 255f), 255);

        // ------------------------------------------------------------------ the wood: four rotten planks, 1 m by 0.5 m
        static void PaintWood()
        {
            const int W = 512, H = 256;
            var L = new Lattice(101);
            var rnd = new System.Random(5);
            var alb = new Color32[W * H]; var mk = new Color32[W * H]; var hg = new float[W * H];
            var tone = new float[4]; var off = new float[4]; var joint = new float[4];
            for (int p = 0; p < 4; p++) { tone[p] = 0.80f + 0.40f * (float)rnd.NextDouble(); off[p] = (float)rnd.NextDouble(); joint[p] = 0.12f + 0.76f * (float)rnd.NextDouble(); }
            // splits along the grain: plank, centre across the plank, start, length
            var cracks = new List<Vector4>();
            for (int i = 0; i < 9; i++) cracks.Add(new Vector4(rnd.Next(4), 0.18f + 0.64f * (float)rnd.NextDouble(), (float)rnd.NextDouble(), 0.10f + 0.28f * (float)rnd.NextDouble()));
            // nails: at the ends of each plank and beside the butt joint (plank, u, across)
            var nails = new List<Vector3>();
            for (int p = 0; p < 4; p++)
                foreach (float nu in new[] { 0.035f, 0.965f, joint[p] - 0.04f, joint[p] + 0.04f })
                    foreach (float nv in new[] { 0.22f, 0.78f }) nails.Add(new Vector3(p, Mathf.Repeat(nu, 1f), nv));
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                    int p = Mathf.Min(3, (int)(v * 4f));
                    float vv = v * 4f - p;                                  // across the plank, 0..1
                    float uo = u + off[p];
                    float warp = L.Fbm(uo, vv * 0.5f + p * 0.37f, 2, 2, 2, 1) - 0.5f;
                    float streak = L.Fbm(uo, vv + warp * 0.18f + p * 0.13f, 3, 36, 3, 2);
                    float ring = 0.5f + 0.5f * Mathf.Sin((vv * 7f + warp * 6f + p * 1.9f) * Mathf.PI);
                    ring *= ring;
                    float grain = streak * 0.7f + ring * 0.3f;
                    float wet = L.Fbm(u, v, 3, 3, 4, 3);
                    float mold = SS(0.58f, 0.80f, L.Fbm(u + 0.37f, v * 1.4f, 4, 3, 4, 5));
                    float soot = SS(0.55f, 0.80f, L.Fbm(u + 0.71f, v, 2, 2, 4, 7));
                    float drip = SS(0.55f, 0.85f, L.Fbm(u, v, 30, 2, 2, 9));     // long vertical water marks
                    var c = Vector3.Lerp(new Vector3(0.085f, 0.055f, 0.036f), new Vector3(0.235f, 0.150f, 0.095f), grain) * (tone[p] * (0.78f + 0.44f * wet));
                    c = Vector3.Lerp(c, new Vector3(0.150f, 0.160f, 0.130f), mold * 0.42f);     // grey-green mildew
                    c *= 1f - 0.50f * soot;
                    c *= 1f - 0.22f * drip;
                    float edge = Mathf.Min(vv, 1f - vv);
                    float seam = 1f - SS(0f, 0.05f, edge);                                       // the gap between two planks
                    float dj = Mathf.Abs(Mathf.Repeat(u - joint[p] + 0.5f, 1f) - 0.5f);
                    float jt = 1f - SS(0f, 0.007f, dj);                                          // the butt joint
                    float cr = 0f;
                    for (int k = 0; k < cracks.Count; k++)
                    {
                        var ck = cracks[k];
                        if ((int)ck.x != p) continue;
                        float du = Mathf.Repeat(u - ck.z, 1f);
                        if (du >= ck.w) continue;
                        float t = du / ck.w;
                        float yc = ck.y + 0.05f * Mathf.Sin(t * 9f + ck.z * 20f) + 0.03f * (L.At(t, ck.y, 6, 4, 11) - 0.5f);
                        float wd = 0.020f * Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.6f);
                        cr = Mathf.Max(cr, 1f - SS(0f, wd, Mathf.Abs(vv - yc)));
                    }
                    float nail = 0f, head = 0f;
                    for (int k = 0; k < nails.Count; k++)
                    {
                        var nl = nails[k];
                        if ((int)nl.x != p) continue;
                        float du = (Mathf.Repeat(u - nl.y + 0.5f, 1f) - 0.5f) * 8f, dv = vv - nl.z;       // u spans 1 m and the plank 0.125 m: scale u by 8 to get round heads
                        float d = Mathf.Sqrt(du * du + dv * dv);
                        nail = Mathf.Max(nail, 1f - SS(0.045f, 0.10f, d));
                        head = Mathf.Max(head, 1f - SS(0.0f, 0.06f, d));
                    }
                    c = Vector3.Lerp(c, new Vector3(0.20f, 0.10f, 0.055f), nail * 0.55f);      // rust halo around the nail
                    c *= 1f - 0.65f * head * (1f - nail * 0.2f);
                    c *= (1f - 0.82f * seam) * (1f - 0.70f * jt) * (1f - 0.90f * cr);
                    int i = y * W + x;
                    alb[i] = C(c);
                    hg[i] = (streak - 0.5f) * 0.10f + (ring - 0.5f) * 0.06f - seam * 0.55f - jt * 0.5f - cr * 0.65f + head * 0.12f - nail * 0.05f;
                    float ao = 1f - 0.75f * seam - 0.5f * jt - 0.85f * cr;
                    float sm = Mathf.Clamp01(0.14f + 0.20f * (1f - grain) + 0.10f * wet - 0.06f * soot);
                    mk[i] = new Color32(0, (byte)(Mathf.Clamp01(ao) * 255f), 0, (byte)(sm * 255f));
                }
            woodAlb = Pack("Scrigno_LegnoAlb", W, H, alb, false);
            woodNor = NormalFrom("Scrigno_LegnoNor", W, H, hg, 3.2f);
            woodMask = Pack("Scrigno_LegnoMask", W, H, mk, true);
        }

        // ------------------------------------------------------------------ the iron: gun-metal, pitted, rust blooms and drips
        static void PaintIron()
        {
            const int S = 256;
            var L = new Lattice(202);
            var alb = new Color32[S * S]; var mk = new Color32[S * S]; var hg = new float[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                    float body = L.Fbm(u, v, 5, 5, 4, 1);
                    float hammer = L.Fbm(u, v, 14, 14, 2, 3);
                    float bloom = SS(0.50f, 0.74f, L.Fbm(u, v, 4, 4, 5, 5) + 0.14f * L.Fbm(u, v, 3, 40, 2, 7) - 0.06f);
                    float crust = L.Fbm(u, v, 24, 24, 3, 9);
                    float drip = SS(0.58f, 0.85f, L.Fbm(u, v, 36, 3, 2, 11)) * (0.4f + 0.6f * bloom);
                    float pit = Hash(x, y, 3) < 0.0045f ? 1f : 0f;
                    float scratch = SS(0.93f, 0.99f, L.Fbm(u, v, 3, 60, 1, 13)) * 0.6f;
                    var metal = Vector3.Lerp(new Vector3(0.095f, 0.095f, 0.105f), new Vector3(0.215f, 0.210f, 0.215f), body * 0.7f + hammer * 0.3f);
                    var rust = Vector3.Lerp(new Vector3(0.200f, 0.085f, 0.040f), new Vector3(0.420f, 0.205f, 0.085f), crust);
                    var c = Vector3.Lerp(metal, rust, Mathf.Clamp01(bloom * 0.92f + drip * 0.55f));
                    c = Vector3.Lerp(c, new Vector3(0.30f, 0.30f, 0.31f), scratch * 0.4f);
                    c *= 1f - 0.7f * pit;
                    int i = y * S + x;
                    alb[i] = C(c);
                    hg[i] = (hammer - 0.5f) * 0.10f + bloom * (crust - 0.4f) * 0.32f - pit * 0.5f - scratch * 0.12f;
                    float rusty = Mathf.Clamp01(bloom * 0.92f + drip * 0.55f);
                    float metallic = (1f - rusty) * 0.88f + 0.04f;
                    float smooth = Mathf.Clamp01((1f - rusty) * 0.46f + 0.10f - 0.12f * hammer + scratch * 0.12f);
                    mk[i] = new Color32((byte)(metallic * 255f), (byte)(Mathf.Clamp01(1f - 0.55f * pit - 0.18f * rusty) * 255f), 0, (byte)(smooth * 255f));
                }
            ironAlb = Pack("Scrigno_FerroAlb", S, S, alb, false);
            ironNor = NormalFrom("Scrigno_FerroNor", S, S, hg, 3.0f);
            ironMask = Pack("Scrigno_FerroMask", S, S, mk, true);
        }

        // ------------------------------------------------------------------ the lining: dried-blood velvet gone black, stained and moth-eaten
        static void PaintLining()
        {
            const int S = 128;
            var L = new Lattice(303);
            var alb = new Color32[S * S]; var mk = new Color32[S * S]; var hg = new float[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                    float mottle = L.Fbm(u, v, 3, 3, 4, 1);
                    float dust = SS(0.55f, 0.78f, L.Fbm(u, v, 4, 4, 3, 3));
                    float hole = Hash(x / 3, y / 3, 7) < 0.012f ? 1f : 0f;
                    float weave = 0.5f + 0.25f * Mathf.Sin(x * Mathf.PI * 0.5f) * Mathf.Sin(y * Mathf.PI * 0.5f);
                    var c = Vector3.Lerp(new Vector3(0.050f, 0.012f, 0.020f), new Vector3(0.170f, 0.030f, 0.050f), mottle);
                    c = Vector3.Lerp(c, new Vector3(0.150f, 0.130f, 0.130f), dust * 0.35f);        // dust on the nap
                    c *= 0.88f + 0.24f * weave;
                    c *= 1f - 0.85f * hole;
                    int i = y * S + x;
                    alb[i] = C(c);
                    hg[i] = weave * 0.25f - hole * 0.4f + (mottle - 0.5f) * 0.1f;
                    mk[i] = new Color32(0, (byte)((1f - 0.7f * hole) * 255f), 0, (byte)(Mathf.Clamp01(0.10f + 0.12f * dust) * 255f));
                }
            liningAlb = Pack("Scrigno_FoderaAlb", S, S, alb, false);
            liningNor = NormalFrom("Scrigno_FoderaNor", S, S, hg, 1.6f);
            liningMask = Pack("Scrigno_FoderaMask", S, S, mk, true);
        }

        // ------------------------------------------------------------------ materials
        /// <summary>A copy of a kit material (so the shader keywords the player build keeps are the same) with our own maps. A plain Lit if the kit is missing.</summary>
        static Material Textured(string kit, string name, Texture2D alb, Texture2D nor, Texture2D mask, Color tint, float bump)
        {
            Material m;
            if (ProcKit.HasMat(kit)) m = new Material(ProcKit.Mat(kit));
            else
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.EnableKeyword("_OCCLUSIONMAP");
            }
            m.name = name;
            m.SetColor("_BaseColor", tint); m.SetColor("_Color", tint);
            foreach (var pn in new[] { "_BaseMap", "_MainTex" }) { if (m.HasProperty(pn)) { m.SetTexture(pn, alb); m.SetTextureScale(pn, Vector2.one); m.SetTextureOffset(pn, Vector2.zero); } }
            m.SetTexture("_BumpMap", nor); m.SetFloat("_BumpScale", bump);
            m.SetTexture("_MetallicGlossMap", mask); m.SetTexture("_OcclusionMap", mask);
            m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 1f); m.SetFloat("_OcclusionStrength", 1f);
            m.SetColor("_EmissionColor", Color.black);
            return m;
        }

        public static void Mats()
        {
            if (Ready) return;
            ShrineKit.Mats();
            PaintWood(); PaintIron(); PaintLining();
            Wood = Textured("kit_legno_scuro", "Scrigno_Legno", woodAlb, woodNor, woodMask, Color.white, 1.0f);
            Iron = Textured("kit_ruggine", "Scrigno_Ferro", ironAlb, ironNor, ironMask, Color.white, 1.0f);
            Lining = Textured("kit_sof_scuro", "Scrigno_Fodera", liningAlb, liningNor, liningMask, Color.white, 0.8f);
            Brass = ShrineKit.Lit("Scrigno_Ottone", new Color(0.30f, 0.20f, 0.075f), 0.8f, 0.30f, true);
            Bone = ShrineKit.Lit("Scrigno_Osso", new Color(0.33f, 0.29f, 0.21f), 0f, 0.22f);
            Bone.EnableKeyword("_EMISSION"); Bone.SetColor("_EmissionColor", Cold * 0.010f);
            CoinA = ShrineKit.Lit("Scrigno_MonetaA", new Color(0.40f, 0.27f, 0.095f), 0.40f, 0.38f);
            CoinB = ShrineKit.Lit("Scrigno_MonetaB", new Color(0.15f, 0.23f, 0.17f), 0.35f, 0.30f);          // verdigris
            foreach (var m in new[] { CoinA, CoinB }) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Cold * 0.012f); }
            Sclera = ShrineKit.Lit("Scrigno_Sclera", new Color(0.34f, 0.20f, 0.08f), 0f, 0.55f);          // un globo ingiallito e arrossato, non bianco
            Sclera.EnableKeyword("_EMISSION"); Sclera.SetColor("_EmissionColor", EyeRed * 0.10f);
            Socket = ShrineKit.Lit("Scrigno_Orbita", new Color(0.010f, 0.008f, 0.008f), 0f, 0.04f);          // nero opaco: col lucido le orbite e la pupilla avevano un riflesso bianco da occhi di cartone
        }

        /// <summary>A dim glass cabochon: 'k' multiplies the glow (the old gems burned at 2.4).</summary>
        public static Material Gem(Color c, float k) => ShrineKit.Emit(c, k);

        // ------------------------------------------------------------------ transparent sheets (floor stain, mist)
        /// <summary>Unlit alpha blend, visible from both sides, no depth write: a dark stain on the floor or a sheet of mist.</summary>
        public static Material AlphaBlend(Texture2D tex, bool additive)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = additive ? "Scrigno_Nebbia" : "Scrigno_Macchia" };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        /// <summary>A soft round puff of mist: white with an alpha made of noise inside a round falloff.</summary>
        public static Texture2D PuffTex()
        {
            var L = new Lattice(404);
            return ShrineKit.SoftTex("ScrignoNebbia", 64, 64, (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f;
                float r = Mathf.Sqrt(x * x + y * y);
                float f = Mathf.Clamp01(1f - r);
                return f * f * (0.35f + 0.65f * L.Fbm(u, v, 3, 3, 3, 1)) * 1.4f;
            });
        }

        /// <summary>A dark irregular stain with soft edges.</summary>
        public static Texture2D StainTex(string name, int seed, float blobbiness)
        {
            var L = new Lattice(seed);
            return ShrineKit.SoftTex(name, 64, 64, (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f;
                float r = Mathf.Sqrt(x * x + y * y) * (1f + blobbiness * (L.Fbm(u, v, 4, 4, 3, 2) - 0.5f));
                float f = 1f - SS(0.25f, 0.95f, r);
                return f;
            });
        }

        // ------------------------------------------------------------------ meshes
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        static void Quad(List<int> t, List<Vector3> v, List<Vector3> n, int a, int b, int c, int d)
        {
            Vector3 want = n[a] + n[b] + n[c] + n[d];
            Vector3 cr = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (cr.sqrMagnitude < 1e-14f) cr = Vector3.Cross(v[c] - v[a], v[d] - v[a]);      // a pole: the first triangle is flat
            if (Vector3.Dot(cr, want) >= 0f) { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }
            else { t.Add(a); t.Add(c); t.Add(b); t.Add(a); t.Add(d); t.Add(c); }
        }

        static Mesh Finish(string name, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t0, List<int> t1 = null, bool tangents = true)
        {
            var m = new Mesh { name = name, subMeshCount = t1 == null ? 1 : 2 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.SetTriangles(t0, 0);
            if (t1 != null) m.SetTriangles(t1, 1);
            m.RecalculateBounds();
            if (tangents) m.RecalculateTangents();
            return m;
        }

        /// <summary>A box centred on the origin; the UVs are in tiles (metres divided by 'tile'), shifted by 'uvOff', so a texture keeps its scale on every face.</summary>
        public static Mesh Box(Vector3 size, Vector2 tile, Vector2 uvOff = default)
        {
            string key = $"box {size.x:F3} {size.y:F3} {size.z:F3} {tile.x:F2} {tile.y:F2} {uvOff.x:F2} {uvOff.y:F2}";
            if (cache.TryGetValue(key, out var have) && have != null) return have;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            Vector3 h = size * 0.5f;
            void Face(Vector3 normal, Vector3 u, Vector3 w, int mode)
            {
                int b = v.Count;
                foreach (var s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
                {
                    Vector3 p = Vector3.Scale(normal, h) + u * s.x + w * s.y;
                    v.Add(p); n.Add(normal);
                    Vector2 q = mode == 0 ? new Vector2(p.x, p.y) : mode == 1 ? new Vector2(p.z, p.y) : new Vector2(p.x, p.z);
                    uv.Add(new Vector2(q.x / tile.x, q.y / tile.y) + uvOff);
                }
                Quad(t, v, n, b, b + 1, b + 2, b + 3);
            }
            Face(Vector3.forward, Vector3.right * h.x, Vector3.up * h.y, 0);
            Face(Vector3.back, Vector3.right * h.x, Vector3.up * h.y, 0);
            Face(Vector3.right, Vector3.forward * h.z, Vector3.up * h.y, 1);
            Face(Vector3.left, Vector3.forward * h.z, Vector3.up * h.y, 1);
            Face(Vector3.up, Vector3.right * h.x, Vector3.forward * h.z, 2);
            Face(Vector3.down, Vector3.right * h.x, Vector3.forward * h.z, 2);
            return cache[key] = Finish(key, v, n, uv, t);
        }

        /// <summary>A barrel vault lying along X: the arc runs from angle 'a0' to 'a1' (0..pi is the whole half), its centre is at (y 0, z 'zc'),
        /// the outside radius is 'rOut' and the wall is 'thick'. The origin is the hinge: the vault rises in +Y and ends in +Z. Sub-mesh 0 is
        /// the outside, the end faces and the cut edges, sub-mesh 1 the inside (the lining). UVs in tiles, the second axis along the arc.</summary>
        public static Mesh Barrel(string name, float len, float rOut, float thick, float zc, Vector2 tile, float a0 = 0f, float a1 = Mathf.PI, int segs = 24)
        {
            if (cache.TryGetValue(name, out var have) && have != null) return have;
            float rIn = Mathf.Max(0f, rOut - thick), hl = len * 0.5f;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var outer = new List<int>(); var inner = new List<int>();
            int Add(Vector3 p, Vector3 nn, Vector2 q) { v.Add(p); n.Add(nn); uv.Add(q); return v.Count - 1; }
            Vector2 Pt(float r, float a) => new Vector2(r * Mathf.Sin(a), zc - r * Mathf.Cos(a));        // (y, z)
            var oi = new int[segs + 1, 2]; var ii = new int[segs + 1, 2];
            for (int j = 0; j <= segs; j++)
            {
                float a = Mathf.Lerp(a0, a1, (float)j / segs), sa = Mathf.Sin(a), ca = Mathf.Cos(a);
                Vector2 po = Pt(rOut, a), pi = Pt(rIn, a);
                for (int s = 0; s < 2; s++)
                {
                    float x = s == 0 ? -hl : hl;
                    oi[j, s] = Add(new Vector3(x, po.x, po.y), new Vector3(0f, sa, -ca), new Vector2(x / tile.x, rOut * a / tile.y));
                    ii[j, s] = Add(new Vector3(x, pi.x, pi.y), new Vector3(0f, -sa, ca), new Vector2(x / tile.x, rIn * a / tile.y));
                }
            }
            for (int j = 0; j < segs; j++)
            {
                Quad(outer, v, n, oi[j, 0], oi[j, 1], oi[j + 1, 1], oi[j + 1, 0]);
                if (rIn > 0.0005f) Quad(inner, v, n, ii[j, 0], ii[j, 1], ii[j + 1, 1], ii[j + 1, 0]);
            }
            for (int s = 0; s < 2; s++)           // the end faces: a crown between the two arcs
            {
                float x = s == 0 ? -hl : hl;
                var nrm = new Vector3(s == 0 ? -1f : 1f, 0f, 0f);
                var oc = new int[segs + 1]; var ic = new int[segs + 1];
                for (int j = 0; j <= segs; j++)
                {
                    float a = Mathf.Lerp(a0, a1, (float)j / segs);
                    Vector2 po = Pt(rOut, a), pi = Pt(rIn, a);
                    oc[j] = Add(new Vector3(x, po.x, po.y), nrm, new Vector2(po.y / tile.x, po.x / tile.y));
                    ic[j] = Add(new Vector3(x, pi.x, pi.y), nrm, new Vector2(pi.y / tile.x, pi.x / tile.y));
                }
                for (int j = 0; j < segs; j++) Quad(outer, v, n, oc[j], oc[j + 1], ic[j + 1], ic[j]);
            }
            void Edge(float a, float sign)        // the cut at one end of the arc: it faces along the arc, away from the wall
            {
                Vector2 po = Pt(rOut, a), pi = Pt(rIn, a);
                var nrm = new Vector3(0f, sign * Mathf.Cos(a), sign * Mathf.Sin(a));
                int p0 = Add(new Vector3(-hl, po.x, po.y), nrm, new Vector2(-hl / tile.x, 0f));
                int p1 = Add(new Vector3(hl, po.x, po.y), nrm, new Vector2(hl / tile.x, 0f));
                int p2 = Add(new Vector3(hl, pi.x, pi.y), nrm, new Vector2(hl / tile.x, thick / tile.y));
                int p3 = Add(new Vector3(-hl, pi.x, pi.y), nrm, new Vector2(-hl / tile.x, thick / tile.y));
                Quad(outer, v, n, p0, p1, p2, p3);
            }
            Edge(a0, -1f); Edge(a1, 1f);
            return cache[name] = Finish(name, v, n, uv, outer, inner.Count > 0 ? inner : new List<int>());
        }

        /// <summary>The upper half of a spherical shell (an eyelid): a dome of outside radius 'r' and wall 'thick' with its pole on +Y and its rim on y = 0.</summary>
        public static Mesh Dome(string name, float r, float thick, int lon = 18, int lat = 8)
        {
            if (cache.TryGetValue(name, out var have) && have != null) return have;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float ri = r - thick;
            int[,] og = new int[lat + 1, lon + 1], ig = new int[lat + 1, lon + 1];
            for (int j = 0; j <= lat; j++)
            {
                float th = j / (float)lat * Mathf.PI * 0.5f, st = Mathf.Sin(th), ct = Mathf.Cos(th);
                for (int i = 0; i <= lon; i++)
                {
                    float ph = i / (float)lon * Mathf.PI * 2f, cp = Mathf.Cos(ph), sp = Mathf.Sin(ph);
                    Vector3 dir = new Vector3(st * cp, ct, st * sp);
                    v.Add(dir * r); n.Add(dir); uv.Add(new Vector2(i / (float)lon, j / (float)lat)); og[j, i] = v.Count - 1;
                    v.Add(dir * ri); n.Add(-dir); uv.Add(new Vector2(i / (float)lon, j / (float)lat)); ig[j, i] = v.Count - 1;
                }
            }
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    Quad(t, v, n, og[j, i], og[j, i + 1], og[j + 1, i + 1], og[j + 1, i]);
                    Quad(t, v, n, ig[j, i], ig[j, i + 1], ig[j + 1, i + 1], ig[j + 1, i]);
                }
            for (int i = 0; i < lon; i++)         // the rim: a flat ring facing down
            {
                int a = og[lat, i], b = og[lat, i + 1], c = ig[lat, i + 1], d = ig[lat, i];
                v.Add(v[a]); n.Add(Vector3.down); uv.Add(Vector2.zero); int a2 = v.Count - 1;
                v.Add(v[b]); n.Add(Vector3.down); uv.Add(Vector2.zero); int b2 = v.Count - 1;
                v.Add(v[c]); n.Add(Vector3.down); uv.Add(Vector2.zero); int c2 = v.Count - 1;
                v.Add(v[d]); n.Add(Vector3.down); uv.Add(Vector2.zero); int d2 = v.Count - 1;
                Quad(t, v, n, a2, b2, c2, d2);
            }
            return cache[name] = Finish(name, v, n, uv, t, null);
        }

        /// <summary>A torus lying flat (axis Y): ring radius 'major', tube radius 'minor'.</summary>
        public static Mesh Torus(string name, float major, float minor, int segMajor = 16, int segMinor = 8)
        {
            if (cache.TryGetValue(name, out var have) && have != null) return have;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            int[,] g = new int[segMajor + 1, segMinor + 1];
            for (int i = 0; i <= segMajor; i++)
            {
                float u = i / (float)segMajor * Mathf.PI * 2f, cu = Mathf.Cos(u), su = Mathf.Sin(u);
                for (int j = 0; j <= segMinor; j++)
                {
                    float w = j / (float)segMinor * Mathf.PI * 2f, cw = Mathf.Cos(w), sw = Mathf.Sin(w);
                    v.Add(new Vector3((major + minor * cw) * cu, minor * sw, (major + minor * cw) * su));
                    n.Add(new Vector3(cw * cu, sw, cw * su));
                    uv.Add(new Vector2(i * major * 4f / segMajor, j / (float)segMinor));
                    g[i, j] = v.Count - 1;
                }
            }
            for (int i = 0; i < segMajor; i++) for (int j = 0; j < segMinor; j++) Quad(t, v, n, g[i, j], g[i + 1, j], g[i + 1, j + 1], g[i, j + 1]);
            return cache[name] = Finish(name, v, n, uv, t, null);
        }

        /// <summary>A cone with its base on y = 0 (radius 1) and its tip at y = 1.</summary>
        public static Mesh Cone(int segs = 10)
        {
            string key = "cone" + segs;
            if (cache.TryGetValue(key, out var have) && have != null) return have;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            int[] b = new int[segs + 1], a = new int[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float p = i / (float)segs * Mathf.PI * 2f, c = Mathf.Cos(p), s = Mathf.Sin(p);
                var nn = new Vector3(c, 1f, s).normalized;
                v.Add(new Vector3(c, 0f, s)); n.Add(nn); uv.Add(new Vector2(i / (float)segs, 0f)); b[i] = v.Count - 1;
                v.Add(new Vector3(0f, 1f, 0f)); n.Add(nn); uv.Add(new Vector2(i / (float)segs, 1f)); a[i] = v.Count - 1;
            }
            for (int i = 0; i < segs; i++) Quad(t, v, n, b[i], b[i + 1], a[i + 1], a[i]);
            return cache[key] = Finish(key, v, n, uv, t, null);
        }

        /// <summary>A unit square in the XY plane facing -Z (like Unity's quad): turn it with Euler(90, 0, 0) to lay it on the floor.</summary>
        public static Mesh Plane()
        {
            const string key = "plane";
            if (cache.TryGetValue(key, out var have) && have != null) return have;
            var v = new List<Vector3> { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            var n = new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            var uv = new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            var m = new Mesh { name = key };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(new List<int> { 0, 2, 1, 2, 3, 1 }, 0);
            m.RecalculateBounds();
            return cache[key] = m;
        }

        /// <summary>A sphere of diameter 1.</summary>
        public static Mesh Sphere(int lon = 12, int lat = 8)
        {
            string key = $"sphere {lon} {lat}";
            if (cache.TryGetValue(key, out var have) && have != null) return have;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            int[,] g = new int[lat + 1, lon + 1];
            for (int j = 0; j <= lat; j++)
            {
                float th = j / (float)lat * Mathf.PI, st = Mathf.Sin(th), ct = Mathf.Cos(th);
                for (int i = 0; i <= lon; i++)
                {
                    float ph = i / (float)lon * Mathf.PI * 2f;
                    var d = new Vector3(st * Mathf.Cos(ph), ct, st * Mathf.Sin(ph));
                    v.Add(d * 0.5f); n.Add(d); uv.Add(new Vector2(i / (float)lon, 1f - j / (float)lat)); g[j, i] = v.Count - 1;
                }
            }
            for (int j = 0; j < lat; j++) for (int i = 0; i < lon; i++) Quad(t, v, n, g[j, i], g[j, i + 1], g[j + 1, i + 1], g[j + 1, i]);
            return cache[key] = Finish(key, v, n, uv, t, null);
        }

        /// <summary>A cylinder along Y, diameter 1 and height 1, centred on the origin.</summary>
        public static Mesh Cyl(int segs = 14)
        {
            string key = "cyl" + segs;
            if (cache.TryGetValue(key, out var have) && have != null) return have;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            int[] bo = new int[segs + 1], to = new int[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float p = i / (float)segs * Mathf.PI * 2f, c = Mathf.Cos(p), s = Mathf.Sin(p);
                v.Add(new Vector3(c * 0.5f, -0.5f, s * 0.5f)); n.Add(new Vector3(c, 0f, s)); uv.Add(new Vector2(i / (float)segs, 0f)); bo[i] = v.Count - 1;
                v.Add(new Vector3(c * 0.5f, 0.5f, s * 0.5f)); n.Add(new Vector3(c, 0f, s)); uv.Add(new Vector2(i / (float)segs, 1f)); to[i] = v.Count - 1;
            }
            for (int i = 0; i < segs; i++) Quad(t, v, n, bo[i], bo[i + 1], to[i + 1], to[i]);
            foreach (float y in new[] { -0.5f, 0.5f })
            {
                int c0 = v.Count; v.Add(new Vector3(0f, y, 0f)); n.Add(new Vector3(0f, Mathf.Sign(y), 0f)); uv.Add(new Vector2(0.5f, 0.5f));
                var ring = new int[segs + 1];
                for (int i = 0; i <= segs; i++)
                {
                    float p = i / (float)segs * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(p) * 0.5f, y, Mathf.Sin(p) * 0.5f)); n.Add(new Vector3(0f, Mathf.Sign(y), 0f)); uv.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(p), 0.5f + 0.5f * Mathf.Sin(p))); ring[i] = v.Count - 1;
                }
                for (int i = 0; i < segs; i++) Quad(t, v, n, c0, ring[i], ring[i + 1], ring[i + 1]);
            }
            return cache[key] = Finish(key, v, n, uv, t, null);
        }

        // ------------------------------------------------------------------ merging
        /// <summary>Collects meshes with their placement, grouped by material, and turns each group into one object with one merged mesh: the
        /// chest is made of a few hundred small pieces, but only about a dozen of them are ever drawn.</summary>
        public sealed class MeshBag
        {
            readonly Dictionary<Material, List<CombineInstance>> parts = new Dictionary<Material, List<CombineInstance>>();
            readonly List<Material> order = new List<Material>();
            public readonly List<Mesh> made = new List<Mesh>();

            public void Add(Material m, Mesh mesh, Vector3 pos, Vector3 euler, Vector3 scale, int sub = 0)
            {
                if (!parts.TryGetValue(m, out var list)) { list = new List<CombineInstance>(); parts[m] = list; order.Add(m); }
                list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = Matrix4x4.TRS(pos, Quaternion.Euler(euler), scale) });
            }
            public void Add(Material m, Mesh mesh, Vector3 pos) => Add(m, mesh, pos, Vector3.zero, Vector3.one);

            /// <summary>Builds the merged objects under 'parent'; returns the renderers (the caller can tint or hide them).</summary>
            public List<MeshRenderer> Emit(Transform parent, string name, ShadowCastingMode shadows)
            {
                var res = new List<MeshRenderer>();
                foreach (var m in order)
                {
                    var go = new GameObject(name + "_" + m.name);
                    go.transform.SetParent(parent, false);
                    var mesh = new Mesh { name = go.name };
                    mesh.CombineMeshes(parts[m].ToArray(), true, true);
                    mesh.RecalculateBounds();
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = m;
                    r.shadowCastingMode = shadows;
                    made.Add(mesh);
                    res.Add(r);
                }
                parts.Clear(); order.Clear();
                return res;
            }
        }
    }
}
