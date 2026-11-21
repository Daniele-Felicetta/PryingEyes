using System;
using System.IO;
using System.IO.Compression;

/// <summary>A tiny RGB picture with lines, discs and a PNG writer (no libraries): the lab draws circuits and rides to look at.</summary>
sealed class Picture
{
    public readonly int w, h;
    readonly byte[] px;

    public Picture(int w, int h, byte r = 24, byte g = 28, byte b = 24)
    {
        this.w = w; this.h = h;
        px = new byte[w * h * 3];
        for (int i = 0; i < w * h; i++) { px[i * 3] = r; px[i * 3 + 1] = g; px[i * 3 + 2] = b; }
    }

    public void Set(int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        int o = (y * w + x) * 3;
        px[o] = r; px[o + 1] = g; px[o + 2] = b;
    }

    public void Disc(float cx, float cy, float radius, byte r, byte g, byte b)
    {
        int x0 = (int)Math.Floor(cx - radius), x1 = (int)Math.Ceiling(cx + radius), y0 = (int)Math.Floor(cy - radius), y1 = (int)Math.Ceiling(cy + radius);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius) Set(x, y, r, g, b);
    }

    public void Line(float x0, float y0, float x1, float y1, byte r, byte g, byte b, float thick = 1f)
    {
        float dx = x1 - x0, dy = y1 - y0;
        int n = (int)Math.Max(1f, Math.Max(Math.Abs(dx), Math.Abs(dy)) * 1.5f);
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            if (thick <= 1.01f) Set((int)Math.Round(x0 + dx * t), (int)Math.Round(y0 + dy * t), r, g, b);
            else Disc(x0 + dx * t, y0 + dy * t, thick * 0.5f, r, g, b);
        }
    }

    public void Save(string path)
    {
        using (var fs = File.Create(path))
        {
            fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var head = new byte[13];
            Put32(head, 0, w); Put32(head, 4, h); head[8] = 8; head[9] = 2;
            Chunk(fs, "IHDR", head);
            var raw = new byte[(w * 3 + 1) * h];
            for (int y = 0; y < h; y++) { raw[y * (w * 3 + 1)] = 0; Buffer.BlockCopy(px, y * w * 3, raw, y * (w * 3 + 1) + 1, w * 3); }
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw, 0, raw.Length);
                Chunk(fs, "IDAT", ms.ToArray());
            }
            Chunk(fs, "IEND", new byte[0]);
        }
    }

    static void Put32(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; Put32(len, 0, data.Length); s.Write(len, 0, 4);
        var t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t, 0, 4);
        s.Write(data, 0, data.Length);
        uint crc = Crc(t, data);
        var c = new byte[4]; Put32(c, 0, (int)crc); s.Write(c, 0, 4);
    }

    static uint[] table;
    static uint Crc(byte[] type, byte[] data)
    {
        if (table == null)
        {
            table = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; table[n] = c; }
        }
        uint crc = 0xFFFFFFFFu;
        foreach (var b in type) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
