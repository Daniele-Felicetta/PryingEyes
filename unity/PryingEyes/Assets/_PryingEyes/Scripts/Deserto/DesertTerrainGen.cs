using System;
using System.Collections.Generic;

namespace PryingEyes.Deserto
{
    /// <summary>A rectangle of the ground that has no sand (m): the well of the stairs that go down into a catacomb.</summary>
    public struct HoleRect
    {
        public float x0, z0, x1, z1;
        public HoleRect(float x0, float z0, float x1, float z1) { this.x0 = x0; this.z0 = z0; this.x1 = x1; this.z1 = z1; }
        public bool Contains(float x, float z) => x > x0 && x < x1 && z > z0 && z < z1;
    }

    /// <summary>The mesh of one square of the desert, as plain arrays (the sand is computed on a worker thread, Unity makes the mesh from these).
    /// Positions are relative to the chunk's corner, so far from the origin the floats keep their precision. The grid is regular except where a
    /// hole has its edges: the lines of the grid go exactly through them, so the hole is cut with no gap and no sliver. Every edge has a skirt that
    /// hangs below it: two neighbours with different steps (the near sand is finer than the far one) never show a crack between them.</summary>
    public sealed class ChunkData
    {
        public int cx, cz;
        public float step;
        public float[] pos;        // x, y, z per vertex
        public float[] nrm;        // x, y, z per vertex
        public int[] tris;
        public int vertexCount;
        public float minY, maxY;
    }

    public static class DesertTerrainGen
    {
        public const float ChunkSize = 64f;
        public const float SkirtDepth = 3.5f;

        /// <summary>The grid lines from a to b, every 'step', plus the lines in 'cuts' (a grid line within 'snap' of a cut moves onto it).</summary>
        public static float[] Axis(float a, float b, float step, List<float> cuts, float snap = 0.35f)
        {
            int n = Math.Max(1, (int)Math.Round((b - a) / step));
            var v = new List<float>(n + 1 + (cuts == null ? 0 : cuts.Count));
            for (int i = 0; i <= n; i++) v.Add(i == n ? b : a + (b - a) * i / n);
            if (cuts != null)
            {
                var placed = new List<float>();
                foreach (float c in cuts)
                {
                    if (c <= a + 0.01f || c >= b - 0.01f) continue;
                    bool twin = false;
                    foreach (float q in placed) if (Math.Abs(q - c) < 0.01f) { twin = true; break; }
                    if (twin) continue;
                    // only the regular lines 1..n-1 can move: the two ends of the chunk never do, and a line that already holds a cut stays
                    int near = -1; float best = snap;
                    for (int i = 1; i < n; i++)
                    {
                        float d = Math.Abs(v[i] - c);
                        if (d < best && !placed.Contains(v[i])) { best = d; near = i; }
                    }
                    if (near >= 0) v[near] = c; else v.Add(c);
                    placed.Add(c);
                }
            }
            v.Sort();
            // two lines closer than 1 cm would make a degenerate sliver (a hole edge is a multiple of 5 cm, so these are real duplicates)
            for (int i = v.Count - 2; i >= 1; i--) if (v[i + 1] - v[i] < 0.01f) v.RemoveAt(i);
            return v.ToArray();
        }

        /// <summary>The sand of chunk (cx, cz) with the given grid step. 'holes' are the rectangles (world metres) that are left open.</summary>
        public static ChunkData Build(int cx, int cz, float step, IList<HoleRect> holes)
        {
            float ox = cx * ChunkSize, oz = cz * ChunkSize;
            var cutX = new List<float>(); var cutZ = new List<float>();
            if (holes != null)
                foreach (var h in holes)
                {
                    if (h.x1 <= ox || h.x0 >= ox + ChunkSize || h.z1 <= oz || h.z0 >= oz + ChunkSize) continue;
                    cutX.Add(h.x0 - ox); cutX.Add(h.x1 - ox); cutZ.Add(h.z0 - oz); cutZ.Add(h.z1 - oz);
                }
            float[] ax = Axis(0f, ChunkSize, step, cutX), az = Axis(0f, ChunkSize, step, cutZ);
            int nx = ax.Length, nz = az.Length;
            float[] hgt = new float[nx * nz];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float y = DesertWorld.Height(ox + ax[i], oz + az[j]);
                    hgt[j * nx + i] = y;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            var d = new ChunkData { cx = cx, cz = cz, step = step, minY = minY - SkirtDepth, maxY = maxY };
            int skirtVerts = 2 * (nx + nz);
            int vc = nx * nz + skirtVerts;
            d.pos = new float[vc * 3]; d.nrm = new float[vc * 3];
            d.vertexCount = vc;
            const float e = 0.6f;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = (j * nx + i) * 3;
                    float wx = ox + ax[i], wz = oz + az[j];
                    d.pos[k] = ax[i]; d.pos[k + 1] = hgt[j * nx + i]; d.pos[k + 2] = az[j];
                    float hx = DesertWorld.Height(wx + e, wz) - DesertWorld.Height(wx - e, wz);
                    float hz = DesertWorld.Height(wx, wz + e) - DesertWorld.Height(wx, wz - e);
                    float nxv = -hx / (2f * e), nzv = -hz / (2f * e);
                    float len = (float)Math.Sqrt(nxv * nxv + 1f + nzv * nzv);
                    d.nrm[k] = nxv / len; d.nrm[k + 1] = 1f / len; d.nrm[k + 2] = nzv / len;
                }
            var tris = new List<int>(nx * nz * 6 + skirtVerts * 6);
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    float cxm = ox + (ax[i] + ax[i + 1]) * 0.5f, czm = oz + (az[j] + az[j + 1]) * 0.5f;
                    bool hole = false;
                    if (holes != null) foreach (var h in holes) if (h.Contains(cxm, czm)) { hole = true; break; }
                    if (hole) continue;
                    int a = j * nx + i, b = a + 1, c = a + nx, dd = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(dd);
                }
            // skirts: a strip that hangs from every edge vertex (facing outwards), so a crack between two chunks shows more sand and not the sky
            int s = nx * nz;
            void Skirt(int[] edge, bool flip)
            {
                int first = s;
                for (int q = 0; q < edge.Length; q++)
                {
                    int src = edge[q] * 3, dst = s * 3;
                    d.pos[dst] = d.pos[src]; d.pos[dst + 1] = d.pos[src + 1] - SkirtDepth; d.pos[dst + 2] = d.pos[src + 2];
                    d.nrm[dst] = d.nrm[src]; d.nrm[dst + 1] = d.nrm[src + 1]; d.nrm[dst + 2] = d.nrm[src + 2];
                    s++;
                }
                for (int q = 0; q < edge.Length - 1; q++)
                {
                    int t0 = edge[q], t1 = edge[q + 1], b0 = first + q, b1 = first + q + 1;
                    if (!flip) { tris.Add(t0); tris.Add(t1); tris.Add(b0); tris.Add(t1); tris.Add(b1); tris.Add(b0); }
                    else { tris.Add(t0); tris.Add(b0); tris.Add(t1); tris.Add(t1); tris.Add(b0); tris.Add(b1); }
                }
            }
            var south = new int[nx]; var north = new int[nx]; var west = new int[nz]; var east = new int[nz];
            for (int i = 0; i < nx; i++) { south[i] = i; north[i] = (nz - 1) * nx + i; }
            for (int j = 0; j < nz; j++) { west[j] = j * nx; east[j] = j * nx + nx - 1; }
            Skirt(south, false); Skirt(north, true); Skirt(west, true); Skirt(east, false);
            d.tris = tris.ToArray();
            return d;
        }
    }
}
