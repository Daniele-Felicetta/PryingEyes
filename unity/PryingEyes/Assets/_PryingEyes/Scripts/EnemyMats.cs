using UnityEngine;

namespace PryingEyes
{
    /// <summary>Materiali PBR procedurali per i nemici meccanici: ferro scuro arrugginito,
    /// composito nero per i droni e emissioni smorzate. Nessun asset scaricato.</summary>
    public static class EnemyMats
    {
        static bool built;
        static Maps armor, drone;

        public static void Ensure()
        {
            if (built) return;
            built = true;
            armor = ArmorMaps();
            drone = DroneMaps();
        }

        class Maps
        {
            public Texture2D albedo, metal, normal;
        }

        public static Material Armor(Color tint) { Ensure(); return Lit(armor, tint, 1.6f, new Vector2(1.6f, 1.6f)); }
        public static Material Drone(Color tint) { Ensure(); return Lit(drone, tint, 1.5f, new Vector2(2.2f, 2.2f)); }

        public static Material Glow(Color c, float intensity)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = Color.black;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            m.SetFloat("_Metallic", 0.2f);
            m.SetFloat("_Smoothness", 0.35f);
            return m;
        }

        static Material Lit(Maps maps, Color tint, float bumpScale, Vector2 tiling)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = tint;
            m.SetTexture("_BaseMap", maps.albedo);
            m.SetTexture("_MetallicGlossMap", maps.metal);
            m.SetTexture("_BumpMap", maps.normal);
            m.SetFloat("_BumpScale", bumpScale);
            m.SetFloat("_Metallic", 0.78f);
            m.SetFloat("_Smoothness", 0.48f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_EMISSION");
            // autoilluminazione minima: nel buio la sagoma resta leggibile senza luci extra
            m.SetColor("_EmissionColor", new Color(0.035f, 0.035f, 0.04f));
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

        static Texture2D Tex(Color32[] px, int S, bool linear)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true, linear)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }

        static Texture2D NormalTex(float[] h, int S, float strength)
        {
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float l = h[y * S + ((x - 1 + S) % S)];
                    float r = h[y * S + ((x + 1) % S)];
                    float d = h[((y - 1 + S) % S) * S + x];
                    float u = h[((y + 1) % S) * S + x];
                    float dx = (l - r) * strength, dy = (d - u) * strength;
                    float inv = 1f / Mathf.Sqrt(dx * dx + dy * dy + 1f);
                    px[y * S + x] = new Color32(
                        B((-dx * inv * 0.5f + 0.5f) * 255f),
                        B((-dy * inv * 0.5f + 0.5f) * 255f),
                        B((inv * 0.5f + 0.5f) * 255f), 255);
                }
            return Tex(px, S, true);
        }

        // ---------- ferro scuro, ruggine e unto ----------
        static Maps ArmorMaps()
        {
            const int S = 512;
            var alb = new Color32[S * S];
            var met = new Color32[S * S];
            var hgt = new float[S * S];
            var rnd = new System.Random(3301);

            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    float n = Hash(x, y, 3301);
                    float v = 54f + (n - 0.5f) * 16f;
                    alb[i] = new Color32(B(v), B(v * 0.97f), B(v * 0.92f), 255);
                    met[i] = new Color32(B((0.67f + (n - 0.5f) * 0.1f) * 255f), 0, 0, B((0.37f + (n - 0.5f) * 0.08f) * 255f));
                    hgt[i] = n * 0.10f;
                }

            // ruggine: chiazze calde e opache
            for (int k = 0; k < 46; k++)
            {
                int cx = rnd.Next(S), cy = rnd.Next(S), r = 8 + rnd.Next(50);
                for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        if (x < 0 || y < 0 || x >= S || y >= S) continue;
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                        if (d > 1f) continue;
                        float a = Mathf.Pow(1f - d, 1.7f) * 0.8f;
                        int i = y * S + x;
                        alb[i] = new Color32((byte)Mathf.Lerp(alb[i].r, 92, a),
                                             (byte)Mathf.Lerp(alb[i].g, 48, a),
                                             (byte)Mathf.Lerp(alb[i].b, 28, a), 255);
                        met[i] = new Color32((byte)Mathf.Lerp(met[i].r, 90, a), 0, 0,
                                             (byte)Mathf.Lerp(met[i].a, 40, a));
                        hgt[i] += a * 0.15f;
                    }
            }

            // giunti e pannelli
            for (int x = 0; x < S; x += 128)
                for (int y = 0; y < S; y++)
                {
                    int i = y * S + x;
                    alb[i] = new Color32(10, 10, 11, 255);
                    hgt[i] -= 0.18f;
                }
            for (int y = 0; y < S; y += 96)
                for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    alb[i] = new Color32(12, 12, 13, 255);
                    hgt[i] -= 0.16f;
                }

            // graffi metallici e rivetti
            for (int k = 0; k < 260; k++)
            {
                float x = rnd.Next(S), y = rnd.Next(S);
                float a = (float)(rnd.NextDouble() * 6.28);
                int len = 3 + rnd.Next(34);
                for (int j = 0; j < len; j++)
                {
                    int px = (int)x, py = (int)y;
                    if (px >= 0 && py >= 0 && px < S && py < S)
                    {
                        int i = py * S + px;
                        float b = 0.3f + rnd.Next(70) / 220f;
                        alb[i] = new Color32((byte)Mathf.Lerp(alb[i].r, 120, b),
                                             (byte)Mathf.Lerp(alb[i].g, 118, b),
                                             (byte)Mathf.Lerp(alb[i].b, 112, b), 255);
                        met[i] = new Color32(B(0.95f * 255f), 0, 0, B(0.75f * 255f));
                        hgt[i] += 0.05f;
                    }
                    x += Mathf.Cos(a); y += Mathf.Sin(a);
                }
            }
            for (int k = 0; k < 110; k++)
            {
                int cx = rnd.Next(S), cy = rnd.Next(S);
                for (int oy = -2; oy <= 2; oy++)
                    for (int ox = -2; ox <= 2; ox++)
                    {
                        int px = cx + ox, py = cy + oy;
                        if (px < 0 || py < 0 || px >= S || py >= S) continue;
                        if (ox * ox + oy * oy > 5) continue;
                        int i = py * S + px;
                        alb[i] = new Color32(80, 78, 74, 255);
                        hgt[i] += 0.2f;
                    }
            }
            // unto e sporco
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    float n = Hash(x, y, 3307);
                    if (n > 0.86f) { alb[i] = new Color32(14, 14, 15, 255); met[i] = new Color32(110, 0, 0, 28); }
                }

            return new Maps { albedo = Tex(alb, S, false), metal = Tex(met, S, true), normal = NormalTex(hgt, S, 2.4f) };
        }

        // ---------- composito nero dei droni ----------
        static Maps DroneMaps()
        {
            const int S = 512;
            var alb = new Color32[S * S];
            var met = new Color32[S * S];
            var hgt = new float[S * S];
            var rnd = new System.Random(4409);

            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    float n = Hash(x, y, 4409);
                    float v = 31f + (n - 0.5f) * 10f;
                    alb[i] = new Color32(B(v), B(v * 1.06f), B(v * 1.16f), 255);
                    met[i] = new Color32(B((0.55f + (n - 0.5f) * 0.1f) * 255f), 0, 0, B((0.52f + (n - 0.5f) * 0.08f) * 255f));
                    hgt[i] = n * 0.06f;
                }

            // pannelli e viti
            for (int x = 0; x < S; x += 64)
                for (int y = 0; y < S; y++)
                {
                    int i = y * S + x;
                    alb[i] = new Color32(5, 6, 8, 255);
                    hgt[i] -= 0.12f;
                }
            for (int y = 0; y < S; y += 64)
                for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    alb[i] = new Color32(6, 7, 9, 255);
                    hgt[i] -= 0.10f;
                }
            for (int k = 0; k < 90; k++)
            {
                int cx = rnd.Next(S), cy = rnd.Next(S);
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int px = cx + ox, py = cy + oy;
                        if (px < 0 || py < 0 || px >= S || py >= S) continue;
                        int i = py * S + px;
                        alb[i] = new Color32(42, 45, 50, 255);
                        hgt[i] += 0.12f;
                    }
            }
            // scrostature chiare, poche
            for (int k = 0; k < 700; k++)
            {
                int x = rnd.Next(S), y = rnd.Next(S), i = y * S + x;
                float a = rnd.Next(100) / 420f;
                alb[i] = new Color32((byte)Mathf.Lerp(alb[i].r, 70, a),
                                     (byte)Mathf.Lerp(alb[i].g, 74, a),
                                     (byte)Mathf.Lerp(alb[i].b, 82, a), 255);
            }
            return new Maps { albedo = Tex(alb, S, false), metal = Tex(met, S, true), normal = NormalTex(hgt, S, 2.0f) };
        }
    }
}
