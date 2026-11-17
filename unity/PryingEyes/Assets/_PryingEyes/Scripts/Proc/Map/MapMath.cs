using System;
using System.Collections.Generic;

namespace PryingEyes.Proc.MapArt
{
    // La mappa del piano e' disegnata a mano da una penna vettoriale: una "penna" scrive triangoli colorati in una Mesh2D, la vista (Unity) li
    // mette in un Graphic dell'interfaccia e il laboratorio (tools/planlab) li rasterizza in un PNG per vederli senza aprire Unity.
    // Questi file sono C# puro (niente UnityEngine): sono compilati da planlab dallo stesso percorso, mai copiati.
    // Unita': i metri del piano, x a destra e y verso nord (la z del piano).

    public struct V2
    {
        public float x, y;
        public V2(float x, float y) { this.x = x; this.y = y; }
        public static V2 operator +(V2 a, V2 b) => new V2(a.x + b.x, a.y + b.y);
        public static V2 operator -(V2 a, V2 b) => new V2(a.x - b.x, a.y - b.y);
        public static V2 operator *(V2 a, float k) => new V2(a.x * k, a.y * k);
        public static V2 operator *(float k, V2 a) => new V2(a.x * k, a.y * k);
        public static V2 operator -(V2 a) => new V2(-a.x, -a.y);
        public float Len => (float)Math.Sqrt(x * x + y * y);
        public float Sqr => x * x + y * y;
        public V2 Perp => new V2(-y, x);
        public V2 Norm { get { float l = Len; return l > 1e-6f ? new V2(x / l, y / l) : new V2(1f, 0f); } }
        public static float Dot(V2 a, V2 b) => a.x * b.x + a.y * b.y;
        public static float Cross(V2 a, V2 b) => a.x * b.y - a.y * b.x;
        public static V2 Lerp(V2 a, V2 b, float t) => new V2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        public static V2 Polar(float radians, float r = 1f) => new V2((float)Math.Cos(radians) * r, (float)Math.Sin(radians) * r);
        public V2 Rot(float radians)
        {
            float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
            return new V2(x * c - y * s, x * s + y * c);
        }
        public override string ToString() => "(" + x.ToString("F2") + "," + y.ToString("F2") + ")";
    }

    /// <summary>Colore RGBA a byte, alfa non premoltiplicato (come i vertici dell'interfaccia di Unity).</summary>
    public struct Col
    {
        public byte r, g, b, a;
        public Col(byte r, byte g, byte b, byte a = 255) { this.r = r; this.g = g; this.b = b; this.a = a; }
        static byte B(float v) => (byte)(v <= 0f ? 0 : v >= 1f ? 255 : (int)(v * 255f + 0.5f));
        public static Col F(float r, float g, float b, float a = 1f) => new Col(B(r), B(g), B(b), B(a));
        /// <summary>Lo stesso colore con un altro alfa (0..1).</summary>
        public Col A(float alpha) => new Col(r, g, b, B(alpha));
        /// <summary>Alfa moltiplicato.</summary>
        public Col MulA(float k) => new Col(r, g, b, B(a / 255f * k));
        /// <summary>Colore moltiplicato (scurisce o schiarisce, l'alfa resta).</summary>
        public Col Mul(float k) => new Col(B(r / 255f * k), B(g / 255f * k), B(b / 255f * k), a);
        public static Col Lerp(Col x, Col y, float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return new Col((byte)(x.r + (y.r - x.r) * t + 0.5f), (byte)(x.g + (y.g - x.g) * t + 0.5f), (byte)(x.b + (y.b - x.b) * t + 0.5f), (byte)(x.a + (y.a - x.a) * t + 0.5f));
        }
        public static Col Mix(Col x, Col y, float t) => Lerp(x, y, t);
    }

    /// <summary>Un rettangolo in metri.</summary>
    public struct R2
    {
        public float x0, y0, x1, y1;
        public R2(float x0, float y0, float x1, float y1) { this.x0 = x0; this.y0 = y0; this.x1 = x1; this.y1 = y1; }
        public float W => x1 - x0;
        public float H => y1 - y0;
        public V2 Center => new V2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
        public bool Contains(V2 p) => p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1;
        public R2 Grow(float m) => new R2(x0 - m, y0 - m, x1 + m, y1 + m);
        public R2 Union(R2 o) => new R2(Math.Min(x0, o.x0), Math.Min(y0, o.y0), Math.Max(x1, o.x1), Math.Max(y1, o.y1));
    }

    /// <summary>Triangoli colorati con una coordinata di texture: quello che il Graphic dell'interfaccia (o il laboratorio) disegna.</summary>
    public sealed class Mesh2D
    {
        public readonly List<V2> pos = new List<V2>();
        public readonly List<V2> uv = new List<V2>();
        public readonly List<Col> col = new List<Col>();
        public readonly List<int> idx = new List<int>();
        public int Count => pos.Count;

        public int Vert(V2 p, V2 t, Col c)
        {
            pos.Add(p); uv.Add(t); col.Add(c);
            return pos.Count - 1;
        }
        public void Tri(int a, int b, int c) { idx.Add(a); idx.Add(b); idx.Add(c); }
        public void Quad(int a, int b, int c, int d) { idx.Add(a); idx.Add(b); idx.Add(c); idx.Add(a); idx.Add(c); idx.Add(d); }
        public void Clear() { pos.Clear(); uv.Clear(); col.Clear(); idx.Clear(); }

        /// <summary>Accoda un'altra mesh, con l'alfa moltiplicato (la rivelazione di una stanza) e uno spostamento.</summary>
        public void Append(Mesh2D o, float alpha = 1f, float dx = 0f, float dy = 0f)
        {
            int b = pos.Count;
            for (int i = 0; i < o.pos.Count; i++)
            {
                pos.Add(new V2(o.pos[i].x + dx, o.pos[i].y + dy));
                uv.Add(o.uv[i]);
                col.Add(alpha >= 0.999f ? o.col[i] : o.col[i].MulA(alpha));
            }
            for (int i = 0; i < o.idx.Count; i++) idx.Add(o.idx[i] + b);
        }

        public R2 Bounds()
        {
            if (pos.Count == 0) return new R2(0, 0, 0, 0);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in pos) { if (p.x < x0) x0 = p.x; if (p.y < y0) y0 = p.y; if (p.x > x1) x1 = p.x; if (p.y > y1) y1 = p.y; }
            return new R2(x0, y0, x1, y1);
        }
    }

    /// <summary>Rumore deterministico (nessuna Random di sistema o di Unity): la stessa mappa esce uguale in ogni macchina e in ogni prova.</summary>
    public static class Noise
    {
        public static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
                return x;
            }
        }
        public static uint Hash(int a, int b, int seed)
        {
            unchecked { return Hash((uint)(a * 374761393) ^ Hash((uint)(b * 668265263) + (uint)seed * 2246822519u)); }
        }
        public static float Rand01(uint h) => (h & 0xFFFFFF) / 16777216f;
        /// <summary>Un numero tra 0 e 1 che dipende solo dai tre interi.</summary>
        public static float R(int a, int b, int seed) => Rand01(Hash(a, b, seed));
        public static float R(int a, int seed) => Rand01(Hash(a, 7919, seed));
        public static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>Rumore di valore a una dimensione, tra -1 e 1.</summary>
        public static float V1(float x, int seed)
        {
            int i = (int)Math.Floor(x);
            float f = x - i;
            float a = R(i, seed) * 2f - 1f, b = R(i + 1, seed) * 2f - 1f;
            return a + (b - a) * Smooth(f);
        }

        /// <summary>Rumore di valore a due dimensioni, tra 0 e 1. 'period' > 0 lo rende periodico (texture che si ripetono senza giunte).</summary>
        public static float V2d(float x, float y, int seed, int period = 0)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = x - ix, fy = y - iy;
            int x1 = ix + 1, y1 = iy + 1;
            if (period > 0) { ix = Wrap(ix, period); iy = Wrap(iy, period); x1 = Wrap(x1, period); y1 = Wrap(y1, period); }
            float a = R(ix, iy, seed), b = R(x1, iy, seed), c = R(ix, y1, seed), d = R(x1, y1, seed);
            float sx = Smooth(fx), sy = Smooth(fy);
            return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy;
        }
        static int Wrap(int v, int p) { v %= p; return v < 0 ? v + p : v; }

        /// <summary>Somma di ottave (0..1, centrato attorno a 0,5), periodica se 'period' > 0 (il periodo vale per la prima ottava).</summary>
        public static float Fbm(float x, float y, int seed, int octaves, int period = 0)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int per = period;
            for (int o = 0; o < octaves; o++)
            {
                sum += V2d(x, y, seed + o * 31, per) * amp;
                norm += amp;
                x *= 2f; y *= 2f; amp *= 0.5f;
                if (per > 0) per *= 2;
            }
            return sum / norm;
        }
    }
}
