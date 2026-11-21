using System.Collections;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The road ribbon: fourteen columns across (barrier, grass, gravel in three strips, paved run-off, kerb, asphalt, kerb, paved run-off, gravel, grass, barrier), one row every two metres, drawn from
    /// Circuit.SurfaceY so that it is the very ground the physics rides on. White edge lines, the chequered start line, the painted grid boxes and the guard rails are drawn on top.</summary>
    public sealed partial class PistaWorld
    {
        const int Cols = 14, Verts = 22;                // columns across, vertices per row (14 + two skirt feet + four for the edge lines + two skirt tops of their own)
        const int SubAsphalt = 0, SubKerbRed = 1, SubKerbWhite = 2, SubApron = 3, SubGravel = 4, SubGrass = 5, SubLine = 6;
        static readonly int[] StripSub = { SubGrass, SubGravel, SubGravel, SubGravel, SubApron, -1, SubAsphalt, -1, SubApron, SubGravel, SubGravel, SubGravel, SubGrass };      // -1: kerb (red or white by row)
        const int ChunkRows = 100;
        const float Lift = 0.014f;

        Vector3 Pt(int i, float d)
        {
            i = c.Wrap(i);
            return new Vector3(c.px[i] + c.nx[i] * d, c.SurfaceY(i, 0f, d), c.pz[i] + c.nz[i] * d);
        }

        Vector3 NormalAt(int i, float d)
        {
            var pn = Pt(i + 1, d) - Pt(i - 1, d);
            var pl = Pt(i, d + 0.25f) - Pt(i, d - 0.25f);
            var n = Vector3.Cross(pn, pl).normalized;
            return n.y < 0f ? -n : n;
        }

        /// <summary>The lateral offsets of the fourteen columns at a row (negative left).</summary>
        void Columns(int i, float[] d)
        {
            i = c.Wrap(i);
            float hw = c.halfW[i];
            float kl = c.kerb[0][i], al = c.apron[0][i], tl = c.trap[0][i], rl = Mathf.Max(c.run[0][i], hw * 0f + 0.5f);
            float kr = c.kerb[1][i], ar = c.apron[1][i], tr = c.trap[1][i], rr = Mathf.Max(c.run[1][i], 0.5f);
            float innerL = Mathf.Min(hw + kl + al, hw + rl), outL = Mathf.Min(hw + rl, innerL + tl);
            float innerR = Mathf.Min(hw + kr + ar, hw + rr), outR = Mathf.Min(hw + rr, innerR + tr);
            float uL = Mathf.Min(0.7f, tl / 3f), uR = Mathf.Min(0.7f, tr / 3f);
            d[6] = -hw; d[5] = -Mathf.Min(hw + kl, innerL); d[4] = -innerL; d[3] = -(innerL + uL); d[2] = -(outL - uL); d[1] = -outL; d[0] = -(hw + rl);
            d[7] = hw; d[8] = Mathf.Min(hw + kr, innerR); d[9] = innerR; d[10] = innerR + uR; d[11] = outR - uR; d[12] = outR; d[13] = hw + rr;
        }

        IEnumerator BuildRoad()
        {
            var allRoad = new[] { mAsphalt, mKerbRed, mKerbWhite, mApron, mGravel, mGrass, mLine };
            var allRail = new[] { mArmco, mPost };
            var d = new float[Cols];
            int chunk = 0;
            for (int i0 = 0; i0 < c.n; i0 += ChunkRows)
            {
                int i1 = Mathf.Min(i0 + ChunkRows, c.n);
                int rows = i1 - i0 + 1;
                var b = new MB(7);
                var vb = new int[rows];
                for (int r = 0; r < rows; r++)
                {
                    int i = c.Wrap(i0 + r);
                    Columns(i, d);
                    vb[r] = b.Count;
                    for (int k = 0; k < Cols; k++)
                    {
                        var p = Pt(i, d[k]);
                        b.Vert(p, NormalAt(i, d[k]), new Vector2(p.x / 4f, p.z / 4f));
                    }
                    // the feet of the skirts, 2.6 m below the barrier line; the texture of a skirt runs along the wall and down it (a plan view smeared over a vertical face is a wall of streaks)
                    float along = (i0 + r) * c.spacing / 4f;
                    for (int s = 0; s < 2; s++)
                    {
                        var top = b.v[vb[r] + (s == 0 ? 0 : 13)];
                        var outward = new Vector3(c.nx[i] * (s == 0 ? -1f : 1f), 0f, c.nz[i] * (s == 0 ? -1f : 1f));
                        b.Vert(top + Vector3.down * 2.6f, outward, new Vector2(along, 2.6f / 4f));
                    }
                    // the edge lines of the asphalt, 30 cm in from the edge, 14 cm wide
                    float hw = c.halfW[i];
                    for (int s = 0; s < 2; s++)
                        for (int e = 0; e < 2; e++)
                        {
                            float dd = (s == 0 ? -1f : 1f) * (hw - 0.30f) + (e == 0 ? -0.07f : 0.07f);       // lateral grows from the first vertex to the second on both sides
                            var p = Pt(i, dd); p.y += Lift;
                            b.Vert(p, Vector3.up, new Vector2(p.x, p.z));
                        }
                    // the tops of the skirts: the barrier line again, with the texture of the wall
                    for (int s = 0; s < 2; s++)
                    {
                        var top = b.v[vb[r] + (s == 0 ? 0 : 13)];
                        var outward = new Vector3(c.nx[i] * (s == 0 ? -1f : 1f), 0f, c.nz[i] * (s == 0 ? -1f : 1f));
                        b.Vert(top, outward, new Vector2(along, 0f));
                    }
                }
                for (int r = 0; r + 1 < rows; r++)
                {
                    int a = vb[r], bq = vb[r + 1];
                    int row = c.Wrap(i0 + r);
                    for (int k = 0; k < Cols - 1; k++)
                    {
                        int sub = StripSub[k] >= 0 ? StripSub[k] : ((row & 1) == 0 ? SubKerbRed : SubKerbWhite);
                        b.Quad(sub, a + k, bq + k, bq + k + 1, a + k + 1);
                    }
                    b.Quad(SubGrass, a + 14, bq + 14, bq + 20, a + 20);
                    b.Quad(SubGrass, a + 15, a + 21, bq + 21, bq + 15);
                    b.Quad(SubLine, a + 16, bq + 16, bq + 17, a + 17);
                    b.Quad(SubLine, a + 18, bq + 18, bq + 19, a + 19);
                }
                Material[] used;
                var mesh = MB.Compact(b, "Strada " + chunk, allRoad, out used);
                made.Add(mesh);
                var g = MB.Spawn("Strada " + chunk, root, mesh, used, false, true);
                g.isStatic = true;
                BuildRails(i0, i1, chunk, allRail);
                chunk++;
                Progress = 0.05f + 0.28f * i0 / c.n;
                if (chunk % 2 == 0) yield return null;
            }
            BuildMarkings();
        }

        // ------------------------------------------------------------------ guard rails
        void BuildRails(int i0, int i1, int chunk, Material[] all)
        {
            var b = new MB(2);
            for (int side = 0; side < 2; side++)
            {
                float sgn = side == 1 ? 1f : -1f;
                for (int i = i0; i < i1; i++)
                {
                    int ia = c.Wrap(i), ib = c.Wrap(i + 1);
                    float la = c.halfW[ia] + Mathf.Max(c.run[side][ia], 0.5f), lb = c.halfW[ib] + Mathf.Max(c.run[side][ib], 0.5f);
                    Vector3 pa = Pt(ia, sgn * la), pb = Pt(ib, sgn * lb);
                    Vector3 inA = new Vector3(c.nx[ia], 0f, c.nz[ia]) * -sgn, inB = new Vector3(c.nx[ib], 0f, c.nz[ib]) * -sgn;      // toward the road
                    Vector3 facing = ((inA + inB) * 0.5f).normalized;
                    // two bands with the profile of a W beam: the middle edge stands 3.5 cm toward the road
                    Vector3 a0 = pa + Vector3.up * 0.46f, b0 = pb + Vector3.up * 0.46f;
                    Vector3 a1 = pa + Vector3.up * 0.62f + inA * 0.035f, b1 = pb + Vector3.up * 0.62f + inB * 0.035f;
                    Vector3 a2 = pa + Vector3.up * 0.79f, b2 = pb + Vector3.up * 0.79f;
                    Vector3 nLow = (facing + Vector3.down * 0.25f).normalized, nHigh = (facing + Vector3.up * 0.25f).normalized;
                    b.Face(0, a0, a1, b1, b0, nLow); b.Face(0, a1, a2, b2, b1, nHigh);
                    Vector3 outA = -inA * 0.10f, outB = -inB * 0.10f;
                    b.Face(0, a2, a2 + outA, b2 + outB, b2, Vector3.up);
                    if ((i & 1) == 0)
                    {
                        Vector3 f = new Vector3(c.tx[ia], 0f, c.tz[ia]).normalized, r = new Vector3(f.z, 0f, -f.x);
                        Vector3 post = pa - inA * 0.16f + Vector3.up * 0.38f;
                        b.Box(1, post, r, Vector3.up, f, new Vector3(0.05f, 0.45f, 0.05f), false);
                    }
                }
            }
            Material[] used;
            var mesh = MB.Compact(b, "Guardrail " + chunk, all, out used);
            made.Add(mesh);
            MB.Spawn("Guardrail " + chunk, root, mesh, used, true, true);
        }

        // ------------------------------------------------------------------ the start line and the grid boxes
        void BuildMarkings()
        {
            var b = new MB(1);
            // chequered line across the asphalt at the start (sample 0), two rows of 50 cm squares
            float hw = c.halfW[0];
            Vector3 t0 = new Vector3(c.tx[0], 0f, c.tz[0]).normalized, n0 = new Vector3(c.nx[0], 0f, c.nz[0]);
            int cols = Mathf.FloorToInt(hw * 2f / 0.5f);
            for (int row = 0; row < 2; row++)
                for (int q = 0; q < cols; q++)
                {
                    if (((row + q) & 1) != 0) continue;
                    float d0 = -hw + q * 0.5f, d1 = d0 + 0.5f;
                    Vector3 p0 = new Vector3(c.px[0], 0f, c.pz[0]) + t0 * (row * 0.5f) + n0 * d0, p1 = new Vector3(c.px[0], 0f, c.pz[0]) + t0 * (row * 0.5f) + n0 * d1;
                    Vector3 p2 = p1 + t0 * 0.5f, p3 = p0 + t0 * 0.5f;
                    p0.y = c.SurfaceY(0, 0f, d0) + Lift * 1.4f; p1.y = c.SurfaceY(0, 0f, d1) + Lift * 1.4f; p2.y = p1.y; p3.y = p0.y;
                    b.Face(0, p0, p3, p2, p1, Vector3.up);
                }
            // the grid: a painted rectangle for each of the twenty places (fifteen rivals, the player and the three friends of a cooperative game)
            for (int k = 0; k < 20; k++)
            {
                float gx, gz, gy, gh;
                c.GridSlot(k, out gx, out gz, out gy, out gh);
                Vector3 f = new Vector3(Mathf.Sin(gh), 0f, Mathf.Cos(gh)), r = new Vector3(f.z, 0f, -f.x);
                Vector3 ctr = new Vector3(gx, gy + Lift * 1.2f, gz) + f * 0.7f;
                float L = 1.1f, W = 0.5f, th = 0.045f;
                MarkRect(b, ctr + f * L, r, f, W + th, th);        // front edge
                MarkRect(b, ctr - f * L, r, f, W + th, th);        // back edge
                MarkRect(b, ctr + r * W, f, r, L, th);             // right edge
                MarkRect(b, ctr - r * W, f, r, L, th);             // left edge
            }
            var mesh = b.ToMesh("Segnaletica");
            made.Add(mesh);
            MB.Spawn("Segnaletica", root, mesh, new[] { mLine }, false, true);
        }

        /// <summary>A thin painted bar centred at 'at': 'along' is the direction of its length (half length 'halfLen'), 'across' the other one (half width 'halfWid').</summary>
        static void MarkRect(MB b, Vector3 at, Vector3 along, Vector3 across, float halfLen, float halfWid)
        {
            Vector3 a = along * halfLen, w = across * halfWid;
            b.Face(0, at - a - w, at - a + w, at + a + w, at + a - w, Vector3.up);
        }
    }
}
