using System.Threading.Tasks;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The two pictures of the trackside, drawn by code once per run on a worker thread (pure arithmetic, like the textures of the ground and of the leaves): an atlas of advertising boards (sixteen
    /// designs of 256 by 96 texels with invented names written in a five by seven letter table) and the crowd (spectators in three rows, cut out of the card: the shader cuts at one half and the mipmaps
    /// are made by hand so that a crowd seen from far does not thin out). Shared by every race, never destroyed with the world.</summary>
    public static class PistaSigns
    {
        public const int CellW = 256, CellH = 96, Cols = 4, Rows = 4, AtlasW = CellW * Cols, AtlasH = CellH * Rows;
        public const int Designs = Cols * Rows;
        public const int CrowdW = 512, CrowdH = 256;
        public static Texture2D Boards { get; private set; }
        public static Texture2D Crowd { get; private set; }
        public static bool Ready { get { return Boards != null && Crowd != null; } }

        sealed class Drawn { public Color32[] boards; public Color32[][] crowd; }
        static Task<Drawn> job;

        /// <summary>Start drawing in the background (calling it again does nothing).</summary>
        public static void Begin()
        {
            if (job != null || Ready) return;
            job = Task.Run(Compute);
        }

        public static bool Done { get { return Ready || job == null || job.IsCompleted; } }

        /// <summary>Turn the finished pictures into textures (main thread, after Done). A failed job leaves them empty: the trackside then has no boards and no crowd.</summary>
        public static void Finish()
        {
            if (Ready || job == null || !job.IsCompleted) return;
            if (job.IsFaulted || job.IsCanceled) { Debug.LogWarning("[PryingEyes] pista: i cartelloni e la folla non sono stati fatti: " + (job.Exception != null ? job.Exception.GetBaseException().Message : "annullato")); job = null; return; }
            var r = job.Result;
            var b = new Texture2D(AtlasW, AtlasH, TextureFormat.RGBA32, true, false) { name = "PistaCartelloni", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            b.SetPixels32(r.boards);
            b.Apply(true, false);
            Boards = b;
            var c = new Texture2D(CrowdW, CrowdH, TextureFormat.RGBA32, true, false) { name = "PistaFolla", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            for (int level = 0; level < r.crowd.Length; level++) c.SetPixels32(r.crowd[level], level);
            c.Apply(false, false);
            Crowd = c;
            job = null;
        }

        /// <summary>The rectangle of a design in the atlas (u0, v0, u1, v1), a few texels in from the edge so that neighbours do not bleed in.</summary>
        public static Vector4 Rect(int design)
        {
            design = ((design % Designs) + Designs) % Designs;
            int col = design % Cols, row = design / Cols;
            const float inset = 5f;
            return new Vector4((col * CellW + inset) / AtlasW, (row * CellH + inset) / AtlasH, ((col + 1) * CellW - inset) / AtlasW, ((row + 1) * CellH - inset) / AtlasH);
        }

        static Drawn Compute() { return new Drawn { boards = DrawBoards(), crowd = PistaFoliage.Mips(DrawCrowd(), CrowdW, CrowdH) }; }

        // ------------------------------------------------------------------ the boards
        static readonly string[] Names =
        {
            "ARGO", "TIGRE", "SCINTILLA", "MONTESOLE", "VALLE ALTA", "PRYING EYES", "GRAN PREMIO", "CORSA",
            "TURBO", "VELOCE", "FRENI SPORT", "OLIO MOTORE", "GOMME PRO", "OFFICINA", "BENZINA SUPER", "CASCHI"
        };

        // background, stripe, text (sRGB 0..255)
        static readonly int[][] Scheme =
        {
            new[] { 190, 25, 30, 20, 20, 20, 255, 255, 255 },       // ARGO
            new[] { 245, 200, 20, 20, 20, 20, 20, 20, 20 },         // TIGRE
            new[] { 20, 70, 170, 240, 240, 240, 255, 255, 255 },    // SCINTILLA
            new[] { 20, 90, 50, 245, 200, 20, 250, 235, 150 },      // MONTESOLE
            new[] { 235, 110, 20, 255, 255, 255, 255, 255, 255 },   // VALLE ALTA
            new[] { 15, 15, 18, 200, 30, 30, 255, 255, 255 },       // PRYING EYES
            new[] { 245, 245, 240, 190, 25, 30, 20, 20, 20 },       // GRAN PREMIO
            new[] { 95, 40, 140, 245, 200, 20, 255, 255, 255 },     // CORSA
            new[] { 20, 20, 20, 245, 200, 20, 245, 200, 20 },       // TURBO
            new[] { 20, 140, 150, 255, 255, 255, 255, 255, 255 },   // VELOCE
            new[] { 235, 235, 235, 30, 30, 30, 200, 30, 30 },       // FRENI SPORT
            new[] { 30, 100, 60, 230, 230, 230, 255, 255, 255 },    // OLIO MOTORE
            new[] { 250, 215, 40, 200, 30, 30, 20, 20, 20 },        // GOMME PRO
            new[] { 30, 60, 120, 235, 110, 20, 255, 255, 255 },     // OFFICINA
            new[] { 200, 30, 30, 245, 245, 245, 245, 245, 245 },    // BENZINA SUPER
            new[] { 20, 20, 20, 235, 110, 20, 255, 255, 255 },      // CASCHI
        };

        // five by seven letters, seven rows of five bits (the top row first, the leftmost column the highest bit)
        const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .-";
        static readonly byte[][] Glyph =
        {
            new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 }, new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E }, new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E },
            new byte[] { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E }, new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F }, new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 },
            new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F }, new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 }, new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E },
            new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C }, new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 }, new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F },
            new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 }, new byte[] { 0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11 }, new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
            new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 }, new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D }, new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 },
            new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E }, new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 }, new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
            new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 }, new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A }, new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 },
            new byte[] { 0x11, 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04 }, new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F },
            new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E }, new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E }, new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F },
            new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E }, new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 }, new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
            new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E }, new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 }, new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E },
            new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
            new byte[] { 0, 0, 0, 0, 0, 0, 0 }, new byte[] { 0, 0, 0, 0, 0, 0x0C, 0x0C }, new byte[] { 0, 0, 0, 0x1F, 0, 0, 0 },
        };

        static Color32 C(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }

        static Color32[] DrawBoards()
        {
            var px = new Color32[AtlasW * AtlasH];
            for (int k = 0; k < Designs; k++) DrawCell(px, (k % Cols) * CellW, (k / Cols) * CellH, k);
            return px;
        }

        static void Block(Color32[] px, int x0, int y0, int w, int h, Color32 c)
        {
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(AtlasH, y0 + h); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(AtlasW, x0 + w); x++) px[y * AtlasW + x] = c;
        }

        static void DrawCell(Color32[] px, int ox, int oy, int k)
        {
            var s = Scheme[k];
            Color32 bg = C(s[0], s[1], s[2]), stripe = C(s[3], s[4], s[5]), ink = C(s[6], s[7], s[8]);
            // the board: a lighter top, a stripe along the bottom and a thin one along the top, a diagonal flash at each end
            for (int y = 0; y < CellH; y++)
            {
                float shade = 0.94f + 0.12f * y / CellH;                         // the picture is y-up: the top of the board is the last row, a little lighter
                var row = new Color32((byte)Mathf.Min(255, Mathf.RoundToInt(bg.r * shade)), (byte)Mathf.Min(255, Mathf.RoundToInt(bg.g * shade)), (byte)Mathf.Min(255, Mathf.RoundToInt(bg.b * shade)), 255);
                for (int x = 0; x < CellW; x++) px[(oy + y) * AtlasW + ox + x] = row;
            }
            Block(px, ox, oy, CellW, 10, stripe);
            Block(px, ox, oy + CellH - 5, CellW, 5, stripe);
            for (int y = 10; y < CellH - 5; y++)
            {
                int w = 26 - (y - 10) * 26 / (CellH - 15);                          // a flash slanting at each end
                Block(px, ox, oy + y, w, 1, stripe);
                Block(px, ox + CellW - w, oy + y, w, 1, stripe);
            }
            string name = Names[k];
            int space = name.IndexOf(' ');
            Color32 shadow = C(0, 0, 0);
            if (space > 0 && name.Length > 8)
            {
                string a = name.Substring(0, space), b2 = name.Substring(space + 1);
                int scale = Mathf.Clamp(Mathf.Min(176 / (Mathf.Max(a.Length, b2.Length) * 6 - 1), 5), 2, 5);
                Text(px, a, ox + CellW / 2, oy + CellH / 2 + 4 * scale + 2, scale, ink, shadow);
                Text(px, b2, ox + CellW / 2, oy + CellH / 2 - 4 * scale - 2, scale, ink, shadow);
            }
            else
            {
                int scale = Mathf.Clamp(176 / (name.Length * 6 - 1), 2, 9);
                Text(px, name, ox + CellW / 2, oy + CellH / 2, scale, ink, shadow);
            }
        }

        /// <summary>A line of letters centred at (cx, cy), 'scale' texels to a dot, with a hard shadow down and to the right.</summary>
        static void Text(Color32[] px, string s, int cx, int cy, int scale, Color32 ink, Color32 shadow)
        {
            int width = s.Length * 6 * scale - scale;
            int x0 = cx - width / 2, y0 = cy - 7 * scale / 2;
            for (int pass = 0; pass < 2; pass++)
            {
                int dx = pass == 0 ? Mathf.Max(1, scale / 2) : 0, dy = pass == 0 ? -Mathf.Max(1, scale / 2) : 0;
                var col = pass == 0 ? shadow : ink;
                for (int i = 0; i < s.Length; i++)
                {
                    int g = Chars.IndexOf(s[i]);
                    if (g < 0) continue;
                    var rows = Glyph[g];
                    for (int r = 0; r < 7; r++)
                        for (int c = 0; c < 5; c++)
                            if ((rows[r] & (1 << (4 - c))) != 0) Block(px, x0 + i * 6 * scale + c * scale + dx, y0 + (6 - r) * scale + dy, scale, scale, col);
                }
            }
        }

        // ------------------------------------------------------------------ the crowd
        static readonly int[][] Shirts =
        {
            new[] { 200, 35, 35 }, new[] { 40, 80, 190 }, new[] { 235, 235, 230 }, new[] { 235, 200, 40 }, new[] { 40, 140, 70 },
            new[] { 35, 35, 40 }, new[] { 235, 120, 30 }, new[] { 110, 170, 230 }, new[] { 230, 120, 160 }, new[] { 140, 140, 145 },
        };
        static readonly int[][] Skins = { new[] { 240, 200, 170 }, new[] { 225, 170, 130 }, new[] { 180, 120, 85 }, new[] { 120, 80, 55 }, new[] { 90, 60, 45 } };
        static readonly int[][] Hairs = { new[] { 30, 22, 16 }, new[] { 70, 45, 25 }, new[] { 150, 110, 60 }, new[] { 210, 190, 120 }, new[] { 40, 40, 40 } };

        static Color32[] DrawCrowd()
        {
            int w = CrowdW, h = CrowdH;
            var px = new Color32[w * h];
            var rng = new System.Random(2026);
            float[] foot = { 100f, 52f, 2f }, size = { 0.80f, 0.90f, 1.0f }, light = { 0.62f, 0.80f, 1.0f };
            for (int row = 0; row < 3; row++)
            {
                const int n = 6;
                float step = w / (float)n;
                for (int k = 0; k < n; k++)
                {
                    float cx = (k + 0.5f + (row == 1 ? 0.5f : 0f) + ((float)rng.NextDouble() - 0.5f) * 0.30f) * step;
                    Person(px, w, h, cx, foot[row], size[row], light[row], rng);
                }
            }
            return px;
        }

        static float Rf(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

        static void Person(Color32[] px, int w, int h, float cx, float foot, float s, float light, System.Random rng)
        {
            var shirt = Shirts[rng.Next(Shirts.Length)]; var skin = Skins[rng.Next(Skins.Length)]; var hair = Hairs[rng.Next(Hairs.Length)];
            float torsoH = 104f * s, torsoW = 74f * s, headR = 21f * s;
            bool arms = rng.NextDouble() < 0.22, cap = rng.NextDouble() < 0.30;
            // wrapped in x: the texture tiles, so a person near an edge is drawn on both sides
            for (int wrap = -1; wrap <= 1; wrap++)
            {
                float x = cx + wrap * w;
                if (x < -80f || x > w + 80f) continue;
                if (arms)
                {
                    Line(px, w, h, x - torsoW * 0.42f, foot + torsoH * 0.80f, x - torsoW * 0.78f, foot + torsoH + headR * 3.0f, 7f * s, Shade(shirt, light * 0.9f));
                    Line(px, w, h, x + torsoW * 0.42f, foot + torsoH * 0.80f, x + torsoW * 0.78f, foot + torsoH + headR * 3.0f, 7f * s, Shade(shirt, light * 0.9f));
                    Disc(px, w, h, x - torsoW * 0.78f, foot + torsoH + headR * 3.1f, 6.5f * s, Shade(skin, light));
                    Disc(px, w, h, x + torsoW * 0.78f, foot + torsoH + headR * 3.1f, 6.5f * s, Shade(skin, light));
                }
                RoundRect(px, w, h, x, foot, torsoW, torsoH, 26f * s, Shade(shirt, light), 0.78f);
                Disc(px, w, h, x, foot + torsoH + headR * 0.55f, headR * 0.55f, Shade(skin, light * 0.9f));                    // the neck
                Disc(px, w, h, x, foot + torsoH + headR * 1.25f, headR, Shade(skin, light));
                if (cap) Cap(px, w, h, x, foot + torsoH + headR * 1.25f, headR, Shade(shirt, light * 0.85f));
                else Cap(px, w, h, x, foot + torsoH + headR * 1.25f, headR * 1.02f, Shade(hair, light));
            }
        }

        static Color32 Shade(int[] c, float k) { return new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(c[0] * k), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(c[1] * k), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(c[2] * k), 0, 255), 255); }

        /// <summary>Put a colour on a pixel with a coverage in 0..1 (the edges blend; the alpha is the largest coverage seen).</summary>
        static void Put(Color32[] px, int w, int h, int x, int y, Color32 c, float cov, float shade)
        {
            if (x < 0 || y < 0 || x >= w || y >= h || cov <= 0f) return;
            int i = y * w + x;
            var old = px[i];
            float oa = old.a / 255f;
            float a = Mathf.Max(oa, cov);
            float t = cov >= 0.999f || oa < 0.001f ? 1f : cov;
            px[i] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(old.r, c.r * shade, t)), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(old.g, c.g * shade, t)), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(old.b, c.b * shade, t)), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
        }

        static void Disc(Color32[] px, int w, int h, float cx, float cy, float r, Color32 c)
        {
            for (int y = Mathf.FloorToInt(cy - r - 1); y <= Mathf.CeilToInt(cy + r + 1); y++)
                for (int x = Mathf.FloorToInt(cx - r - 1); x <= Mathf.CeilToInt(cx + r + 1); x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    Put(px, w, h, x, y, c, Mathf.Clamp01(r - d + 0.5f), 1f);
                }
        }

        /// <summary>The upper part of a disc: a cap or a head of hair.</summary>
        static void Cap(Color32[] px, int w, int h, float cx, float cy, float r, Color32 c)
        {
            for (int y = Mathf.FloorToInt(cy + r * 0.05f); y <= Mathf.CeilToInt(cy + r + 1); y++)
                for (int x = Mathf.FloorToInt(cx - r - 1); x <= Mathf.CeilToInt(cx + r + 1); x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    Put(px, w, h, x, y, c, Mathf.Clamp01(r - d + 0.5f), 1f);
                }
        }

        static void Line(Color32[] px, int w, int h, float ax, float ay, float bx, float by, float width, Color32 c)
        {
            float len = Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            int steps = Mathf.Max(2, Mathf.CeilToInt(len));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Disc(px, w, h, Mathf.Lerp(ax, bx, t), Mathf.Lerp(ay, by, t), width * 0.5f, c);
            }
        }

        /// <summary>A rectangle with round top corners, centred on x, standing on y0; the bottom is darker (the shade at the foot).</summary>
        static void RoundRect(Color32[] px, int w, int h, float cx, float y0, float ww, float hh, float rad, Color32 c, float footShade)
        {
            for (int y = Mathf.FloorToInt(y0); y <= Mathf.CeilToInt(y0 + hh); y++)
                for (int x = Mathf.FloorToInt(cx - ww * 0.5f - 1); x <= Mathf.CeilToInt(cx + ww * 0.5f + 1); x++)
                {
                    float px0 = x + 0.5f - cx, py0 = y + 0.5f - y0;
                    if (py0 < 0f) continue;
                    float qx = Mathf.Abs(px0) - (ww * 0.5f - rad), qy = py0 - (hh - rad);
                    float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - rad;      // the box with round top corners (the foot is cut flat)
                    float cov = Mathf.Clamp01(0.5f - d);
                    Put(px, w, h, x, y, c, cov, Mathf.Lerp(footShade, 1f, Mathf.Clamp01(py0 / hh)));
                }
        }
    }
}
