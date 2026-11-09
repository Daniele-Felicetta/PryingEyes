using UnityEngine;

namespace PryingEyes
{
    /// <summary>Materiali PBR procedurali per le armi: metallo usurato, acciaio, polimero
    /// antiscivolo e legno. Albedo, metallic/smoothness e normal map sono generati a runtime,
    /// senza scaricare né importare asset.</summary>
    public static class GunMats
    {
        public static Material Metal, Steel, Dark, Polymer, Wood, Lens, Dot;
        static bool built;

        public static void Ensure()
        {
            if (built) return;
            built = true;

            var worn = MetalMaps(new Color32(62, 67, 76, 255), 0.62f, 1107);
            var steelMaps = MetalMaps(new Color32(104, 110, 120, 255), 0.72f, 2203);
            var poly = PolymerMaps();
            var wood = WoodMaps();

            Metal = Lit(worn, new Color(0.72f, 0.74f, 0.78f), 1.5f, new Vector2(2f, 1f));
            Steel = Lit(steelMaps, new Color(0.86f, 0.87f, 0.91f), 1.3f, new Vector2(2f, 1f));
            Dark = Lit(worn, new Color(0.36f, 0.37f, 0.41f), 1.7f, new Vector2(2f, 1f));
            Polymer = Lit(poly, Color.white, 1.4f, new Vector2(3f, 3f));
            Wood = Lit(wood, Color.white, 1.1f, new Vector2(2f, 2f));

            Lens = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Lens.color = new Color(0.05f, 0.12f, 0.20f);
            Lens.SetFloat("_Metallic", 0.35f);
            Lens.SetFloat("_Smoothness", 0.72f);

            // punto rosso: emissione HDR, si accende anche con bloom
            Dot = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Dot.color = Color.black;
            Dot.EnableKeyword("_EMISSION");
            Dot.SetColor("_EmissionColor", new Color(4f, 0.15f, 0.08f));
            Dot.SetFloat("_EmissiveIntensity", 3f);
            Dot.SetFloat("_Metallic", 0f);
            Dot.SetFloat("_Smoothness", 0.2f);
        }

        class Maps
        {
            public Texture2D albedo, metal, normal;
        }

        static Material Lit(Maps maps, Color tint, float bumpScale, Vector2 tiling)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = tint;
            m.SetTexture("_BaseMap", maps.albedo);
            m.SetTexture("_MetallicGlossMap", maps.metal);
            m.SetTexture("_BumpMap", maps.normal);
            m.SetFloat("_BumpScale", bumpScale);
            // valori scalari più bassi: con le mappe la smoothness finale e' map.a * _Smoothness
            m.SetFloat("_Metallic", 0.8f);
            m.SetFloat("_Smoothness", 0.62f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.EnableKeyword("_NORMALMAP");
            m.SetTextureScale("_BaseMap", tiling);
            m.SetTextureScale("_MetallicGlossMap", tiling);
            m.SetTextureScale("_BumpMap", tiling);
            return m;
        }

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);

        // ---------- metallo usurato ----------
        static Maps MetalMaps(Color32 baseCol, float smooth, int seed)
        {
            const int S = 512;
            var alb = new Color32[S * S];
            var met = new Color32[S * S];
            var hgt = new float[S * S];
            var rnd = new System.Random(seed);

            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    float n = Hash(x, y, seed);
                    float grain = (Hash(x / 2, y / 2, seed + 1) - 0.5f) * 0.10f;
                    float v = baseCol.r * (1f + grain) + (n - 0.5f) * 10f;
                    alb[i] = new Color32(B(v), B(v * 1.03f), B(v * 1.09f), 255);
                    met[i] = new Color32(B((0.86f + (n - 0.5f) * 0.10f) * 255f), 0, 0,
                        B((smooth + (n - 0.5f) * 0.07f) * 255f));
                    hgt[i] = n * 0.10f;
                }

            // righe di lavorazione (fresatura) lungo X
            for (int y = 0; y < S; y += 9)
                for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    alb[i] = new Color32((byte)(alb[i].r * 0.86f), (byte)(alb[i].g * 0.86f), (byte)(alb[i].b * 0.88f), 255);
                    hgt[i] -= 0.05f;
                }

            // chiazze d'olio: opache, scure, poco metalliche
            for (int k = 0; k < 22; k++)
            {
                int cx = rnd.Next(S), cy = rnd.Next(S), r = 12 + rnd.Next(55);
                for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        if (x < 0 || y < 0 || x >= S || y >= S) continue;
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                        if (d > 1f) continue;
                        float a = Mathf.Pow(1f - d, 2f) * 0.75f;
                        int i = y * S + x;
                        alb[i] = new Color32((byte)Mathf.Lerp(alb[i].r, 12, a),
                                             (byte)Mathf.Lerp(alb[i].g, 13, a),
                                             (byte)Mathf.Lerp(alb[i].b, 15, a), 255);
                        met[i] = new Color32((byte)Mathf.Lerp(met[i].r, 70, a), 0, 0,
                                             (byte)Mathf.Lerp(met[i].a, 45, a));
                        hgt[i] -= a * 0.12f;
                    }
            }

            // graffi: metallo nudo lucido
            for (int k = 0; k < 220; k++)
            {
                float x = rnd.Next(S), y = rnd.Next(S);
                float ang = (float)(rnd.NextDouble() * Mathf.PI * 2);
                int len = 4 + rnd.Next(45);
                float dx = Mathf.Cos(ang), dy = Mathf.Sin(ang);
                for (int j = 0; j < len; j++)
                {
                    int px = (int)x, py = (int)y;
                    if (px >= 0 && py >= 0 && px < S && py < S)
                    {
                        int i = py * S + px;
                        float a = 0.55f + rnd.Next(100) / 260f;
                        alb[i] = new Color32((byte)Mathf.Lerp(alb[i].r, 172, a),
                                             (byte)Mathf.Lerp(alb[i].g, 180, a),
                                             (byte)Mathf.Lerp(alb[i].b, 190, a), 255);
                        met[i] = new Color32(B(0.98f * 255f), 0, 0, B(0.92f * 255f));
                        hgt[i] += 0.06f;
                    }
                    x += dx; y += dy;
                }
            }

            // scheggiature e sporco
            for (int k = 0; k < 900; k++)
            {
                int x = rnd.Next(S), y = rnd.Next(S);
                int i = y * S + x;
                float a = 0.25f + rnd.Next(60) / 100f;
                alb[i] = new Color32((byte)Mathf.Lerp(alb[i].r, 160, a),
                                     (byte)Mathf.Lerp(alb[i].g, 168, a),
                                     (byte)Mathf.Lerp(alb[i].b, 178, a), 255);
            }

            return new Maps
            {
                albedo = Tex(alb, S, false, true),
                metal = Tex(met, S, true, true),
                normal = NormalTex(hgt, S, 2.2f, true),
            };
        }

        // ---------- polimero antiscivolo ----------
        static Maps PolymerMaps()
        {
            const int S = 256;
            var alb = new Color32[S * S];
            var met = new Color32[S * S];
            var hgt = new float[S * S];
            var rnd = new System.Random(771);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    float n = Hash(x, y, 771);
                    float v = 26f + (n - 0.5f) * 10f;
                    alb[i] = new Color32(B(v), B(v + 1f), B(v + 4f), 255);
                    met[i] = new Color32(B(0.05f * 255f), 0, 0, B(0.45f * 255f));
                    hgt[i] = 0f;
                }
            // stippling a piramide
            for (int y = 6; y < S; y += 11)
                for (int x = (y % 22 == 6 ? 6 : 11); x < S; x += 11)
                {
                    for (int oy = -2; oy <= 2; oy++)
                        for (int ox = -2; ox <= 2; ox++)
                        {
                            int px = x + ox, py = y + oy;
                            if (px < 0 || py < 0 || px >= S || py >= S) continue;
                            int i = py * S + px;
                            float d = Mathf.Sqrt(ox * ox + oy * oy) / 3f;
                            float a = Mathf.Clamp01(1f - d);
                            alb[i] = new Color32((byte)Mathf.Lerp(alb[i].r, 58, a * 0.8f),
                                                 (byte)Mathf.Lerp(alb[i].g, 60, a * 0.8f),
                                                 (byte)Mathf.Lerp(alb[i].b, 66, a * 0.8f), 255);
                            hgt[i] = Mathf.Max(hgt[i], a * 0.8f);
                        }
                }
            // polvere e micro-graffi
            for (int k = 0; k < 1400; k++)
            {
                int x = rnd.Next(S), y = rnd.Next(S), i = y * S + x;
                float a = rnd.Next(100) / 300f;
                alb[i] = new Color32((byte)Mathf.Lerp(alb[i].r, 70, a),
                                     (byte)Mathf.Lerp(alb[i].g, 70, a),
                                     (byte)Mathf.Lerp(alb[i].b, 74, a), 255);
            }
            return new Maps
            {
                albedo = Tex(alb, S, false, true),
                metal = Tex(met, S, true, true),
                normal = NormalTex(hgt, S, 2.8f, true),
            };
        }

        // ---------- legno per il fucile a pompa ----------
        static Maps WoodMaps()
        {
            const int S = 256;
            var alb = new Color32[S * S];
            var met = new Color32[S * S];
            var hgt = new float[S * S];
            var rnd = new System.Random(991);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    float n = Hash(x, y, 991);
                    float warp = Mathf.Sin((y + Mathf.Sin(x * 0.05f) * 6f) * 0.16f) * 0.5f + 0.5f;
                    float grain = Mathf.Lerp(0.18f, 1f, warp) * (0.85f + n * 0.3f);
                    float v = (72f + 52f * grain) * (1f + (n - 0.5f) * 0.08f);
                    alb[i] = new Color32(B(v * 1.12f), B(v * 0.72f), B(v * 0.42f), 255);
                    met[i] = new Color32(0, 0, 0, B((0.38f + grain * 0.22f) * 255f));
                    hgt[i] = grain * 0.35f + n * 0.06f;
                }
            // nodi e venature scure
            for (int k = 0; k < 7; k++)
            {
                int cx = rnd.Next(S), cy = rnd.Next(S);
                for (int ring = 3; ring < 26; ring += 2)
                    for (int a = 0; a < 64; a++)
                    {
                        float th = a / 64f * Mathf.PI * 2f;
                        int x = cx + (int)(Mathf.Cos(th) * ring), y = cy + (int)(Mathf.Sin(th) * ring * 0.55f);
                        if (x < 0 || y < 0 || x >= S || y >= S) continue;
                        int i = y * S + x;
                        alb[i] = new Color32(B(alb[i].r * 0.58f), B(alb[i].g * 0.52f), B(alb[i].b * 0.48f), 255);
                    }
            }
            return new Maps
            {
                albedo = Tex(alb, S, false, true),
                metal = Tex(met, S, true, true),
                normal = NormalTex(hgt, S, 1.8f, true),
            };
        }

        static Texture2D Tex(Color32[] px, int S, bool linear, bool mips)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, mips, linear)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(px);
            t.Apply(mips, false);
            return t;
        }

        static Texture2D NormalTex(float[] h, int S, float strength, bool linear)
        {
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float l = h[y * S + ((x - 1 + S) % S)];
                    float r = h[y * S + ((x + 1) % S)];
                    float d = h[((y - 1 + S) % S) * S + x];
                    float u = h[((y + 1) % S) * S + x];
                    float dx = (l - r) * strength;
                    float dy = (d - u) * strength;
                    float inv = 1f / Mathf.Sqrt(dx * dx + dy * dy + 1f);
                    px[y * S + x] = new Color32(
                        B((-dx * inv * 0.5f + 0.5f) * 255f),
                        B((-dy * inv * 0.5f + 0.5f) * 255f),
                        B((inv * 0.5f + 0.5f) * 255f), 255);
                }
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true, linear)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }
    }
}
