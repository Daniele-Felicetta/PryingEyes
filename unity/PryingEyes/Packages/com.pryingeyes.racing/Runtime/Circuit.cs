using System;

namespace Production.Racing
{
    /// <summary>Where a point lies relative to the centreline of the circuit: the segment, how far along it, how far to the right (negative = left) and the distance along the lap.</summary>
    public struct Proj
    {
        public int i;
        public float t;
        /// <summary>Signed lateral offset from the centreline (m), positive to the right of the direction of travel.</summary>
        public float d;
        /// <summary>Distance along the lap from the start line (m).</summary>
        public float s;
        /// <summary>Unit tangent and unit right-hand normal of the centreline here.</summary>
        public float tx, tz, nx, nz;
    }

    /// <summary>A closed race circuit: a centreline sampled every two metres with width, elevation, banking, kerbs and run-off, and every question the bike physics, the rider AI and the picture of the track
    /// need to ask (where am I, what is under the tire, where is the wall, how high is the ground). It is also the ground the bike physics runs on (IGround). Pure code: the same circuit comes out of the same
    /// seed on every machine.
    ///
    /// Across the road, from the middle outward: asphalt (half width), kerb (only on corners), paved run-off, gravel trap (only on some corners), grass, then the barrier. Sample 0 is the start line.</summary>
    public sealed class Circuit : IGround
    {
        public int seed;
        public int n;
        /// <summary>Distance between samples (m) and the length of the lap (m).</summary>
        public float spacing, length;
        public float[] px, pz, py, tx, tz, nx, nz, curv, halfW, bank, vcurv, s;
        /// <summary>Per sample and side (0 left, 1 right): kerb width, paved run-off width, gravel trap width, distance from the asphalt edge to the barrier (m).</summary>
        public float[][] kerb = new float[2][], apron = new float[2][], trap = new float[2][], run = new float[2][];
        public bool clockwise;
        public string name;
        /// <summary>How wet the track is: 0 dry, 1 raining (set before the race is made: the riders plan with it and the bikes feel it).</summary>
        public float wet;
        /// <summary>What the water takes from the grip of each kind of ground at wet = 1 (indexed by SurfaceKind: asphalt, kerb, apron, grass, gravel): painted kerbs and the paved run-off go slippery
        /// first, grass turns to mud, a gravel trap hardly changes.</summary>
        static readonly float[] WetLoss = { 0.30f, 0.42f, 0.34f, 0.45f, 0.10f };
        /// <summary>The share of the dry grip that clean asphalt has now (1 dry, 0.7 in the rain): the number the riders' plan and the measured curves of the bikes use.</summary>
        public float GripScale { get { return 1f - WetLoss[0] * wet; } }
        /// <summary>How many rows of the grid fit behind the line and the index of the longest straight's middle (for the picture).</summary>
        public float minX, maxX, minZ, maxZ;

        const int SearchWin = 10;
        const float GridCell = 40f;
        int gridW, gridH;
        int[][] grid;

        public float TotalWidth(int i, int side) { return halfW[i] + kerb[side][i] + apron[side][i] + trap[side][i]; }

        public int Wrap(int i) { i %= n; return i < 0 ? i + n : i; }

        /// <summary>Wrap a distance along the lap into [0, length).</summary>
        public float WrapS(float v) { v %= length; return v < 0f ? v + length : v; }

        /// <summary>The signed shortest distance from 'from' to 'to' along the lap (positive = ahead).</summary>
        public float DeltaS(float from, float to)
        {
            float d = (to - from) % length;
            if (d > length * 0.5f) d -= length; else if (d < -length * 0.5f) d += length;
            return d;
        }

        /// <summary>Finish building after the arrays are filled: bounds, tangents' cousins, the lookup grid.</summary>
        public void Finish()
        {
            minX = minZ = float.MaxValue; maxX = maxZ = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float w = halfW[i] + 30f;
                minX = Math.Min(minX, px[i] - w); maxX = Math.Max(maxX, px[i] + w); minZ = Math.Min(minZ, pz[i] - w); maxZ = Math.Max(maxZ, pz[i] + w);
            }
            gridW = (int)((maxX - minX) / GridCell) + 2; gridH = (int)((maxZ - minZ) / GridCell) + 2;
            var lists = new System.Collections.Generic.List<int>[gridW * gridH];
            for (int i = 0; i < n; i++)
            {
                int cx = (int)((px[i] - minX) / GridCell), cz = (int)((pz[i] - minZ) / GridCell);
                int k = cz * gridW + cx;
                if (lists[k] == null) lists[k] = new System.Collections.Generic.List<int>();
                lists[k].Add(i);
            }
            grid = new int[lists.Length][];
            for (int k = 0; k < lists.Length; k++) grid[k] = lists[k] == null ? null : lists[k].ToArray();
        }

        // ------------------------------------------------------------------ where am I
        float SegDist2(int i, float qx, float qz, out float t)
        {
            int j = i + 1 == n ? 0 : i + 1;
            float ex = px[j] - px[i], ez = pz[j] - pz[i];
            float wx = qx - px[i], wz = qz - pz[i];
            float e2 = ex * ex + ez * ez;
            t = e2 > 1e-9f ? (wx * ex + wz * ez) / e2 : 0f;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            float cx = wx - ex * t, cz = wz - ez * t;
            return cx * cx + cz * cz;
        }

        /// <summary>Project a point on the centreline. 'hint' is the segment where the same wheel was a moment ago (keeps the search local; pass any value, negative for none).</summary>
        public void Project(float qx, float qz, ref int hint, out Proj r)
        {
            int best = -1; float bestT = 0f, bestD2 = float.MaxValue;
            if (hint >= 0 && hint < n)
            {
                int centre = hint;
                for (int round = 0; round < 24; round++)
                {
                    best = -1; bestD2 = float.MaxValue;
                    for (int k = -SearchWin; k <= SearchWin; k++)
                    {
                        int i = Wrap(centre + k);
                        float t;
                        float d2 = SegDist2(i, qx, qz, out t);
                        if (d2 < bestD2) { bestD2 = d2; best = i; bestT = t; }
                    }
                    int off = best - centre;
                    if (off > n / 2) off -= n; else if (off < -n / 2) off += n;
                    if (off > SearchWin - 2 || off < -(SearchWin - 2)) { centre = best; continue; }
                    break;
                }
                if (bestD2 > 90f * 90f) best = -1;
            }
            if (best < 0)
            {
                bestD2 = float.MaxValue;
                int cx = (int)((qx - minX) / GridCell), cz = (int)((qz - minZ) / GridCell);
                for (int ring = 1; ring <= 3 && best < 0; ring++)
                    for (int gz = cz - ring; gz <= cz + ring; gz++)
                        for (int gx = cx - ring; gx <= cx + ring; gx++)
                        {
                            if (gx < 0 || gz < 0 || gx >= gridW || gz >= gridH) continue;
                            var cell = grid[gz * gridW + gx];
                            if (cell == null) continue;
                            foreach (int idx in cell)
                                for (int o = -1; o <= 0; o++)
                                {
                                    int i = Wrap(idx + o);
                                    float t;
                                    float d2 = SegDist2(i, qx, qz, out t);
                                    if (d2 < bestD2) { bestD2 = d2; best = i; bestT = t; }
                                }
                        }
                if (best < 0)
                    for (int i = 0; i < n; i++)
                    {
                        float t;
                        float d2 = SegDist2(i, qx, qz, out t);
                        if (d2 < bestD2) { bestD2 = d2; best = i; bestT = t; }
                    }
            }
            hint = best;
            int j = best + 1 == n ? 0 : best + 1;
            float ntx = tx[best] + (tx[j] - tx[best]) * bestT, ntz = tz[best] + (tz[j] - tz[best]) * bestT;
            float tl = (float)Math.Sqrt(ntx * ntx + ntz * ntz);
            if (tl > 1e-6f) { ntx /= tl; ntz /= tl; }
            r.i = best; r.t = bestT; r.tx = ntx; r.tz = ntz; r.nx = ntz; r.nz = -ntx;
            float cxp = px[best] + (px[j] - px[best]) * bestT, czp = pz[best] + (pz[j] - pz[best]) * bestT;
            r.d = (qx - cxp) * r.nx + (qz - czp) * r.nz;
            r.s = (best + bestT) * spacing;
        }

        /// <summary>Project a point on the centreline only if the circuit is near (a sample within roughly 160 m, found through the lookup grid): the far countryside is not searched sample by sample.
        /// Returns false when nothing is near. The grid is searched one ring further than the first hit, so the nearest section wins when two run side by side.</summary>
        public bool ProjectNear(float qx, float qz, out Proj r)
        {
            int best = -1; float bestT = 0f, bestD2 = float.MaxValue;
            int cx = (int)Math.Floor((qx - minX) / GridCell), cz = (int)Math.Floor((qz - minZ) / GridCell);
            int hitRing = -1;
            for (int ring = 0; ring <= 5; ring++)
            {
                if (hitRing >= 0 && ring > hitRing + 1) break;
                for (int gz = cz - ring; gz <= cz + ring; gz++)
                    for (int gx = cx - ring; gx <= cx + ring; gx++)
                    {
                        if (ring > 0 && gz > cz - ring && gz < cz + ring && gx > cx - ring && gx < cx + ring) continue;       // only the new ring
                        if (gx < 0 || gz < 0 || gx >= gridW || gz >= gridH) continue;
                        var cell = grid[gz * gridW + gx];
                        if (cell == null) continue;
                        foreach (int idx in cell)
                            for (int o = -1; o <= 0; o++)
                            {
                                int i = Wrap(idx + o);
                                float t;
                                float d2 = SegDist2(i, qx, qz, out t);
                                if (d2 < bestD2) { bestD2 = d2; best = i; bestT = t; }
                            }
                    }
                if (best >= 0 && hitRing < 0) hitRing = ring;
            }
            if (best < 0 || bestD2 > 160f * 160f) { r = default(Proj); return false; }
            int j = best + 1 == n ? 0 : best + 1;
            float ntx = tx[best] + (tx[j] - tx[best]) * bestT, ntz = tz[best] + (tz[j] - tz[best]) * bestT;
            float tl = (float)Math.Sqrt(ntx * ntx + ntz * ntz);
            if (tl > 1e-6f) { ntx /= tl; ntz /= tl; }
            r.i = best; r.t = bestT; r.tx = ntx; r.tz = ntz; r.nx = ntz; r.nz = -ntx;
            float cxp = px[best] + (px[j] - px[best]) * bestT, czp = pz[best] + (pz[j] - pz[best]) * bestT;
            r.d = (qx - cxp) * r.nx + (qz - czp) * r.nz;
            r.s = (best + bestT) * spacing;
            return true;
        }

        // ------------------------------------------------------------------ interpolated per-sample values
        public float Lerp(float[] a, int i, float t) { int j = i + 1 == n ? 0 : i + 1; return a[i] + (a[j] - a[i]) * t; }

        public float HalfWidthAt(Proj r) { return Lerp(halfW, r.i, r.t); }

        /// <summary>Half the width of the asphalt at a distance along the lap.</summary>
        public float HalfWidth(float sAlong) { float f = WrapS(sAlong) / spacing; int i = Wrap((int)f); return Lerp(halfW, i, f - (int)f); }

        /// <summary>The distance from the centreline to the barrier on a side (0 left, 1 right) at a projected point.</summary>
        public float BarrierOffset(Proj r, int side)
        {
            return Lerp(halfW, r.i, r.t) + Lerp(kerb[side], r.i, r.t) * 0f + Lerp(run[side], r.i, r.t);
        }

        // ------------------------------------------------------------------ IGround
        public void Sample(float x, float z, ref int hint, out GroundSample g)
        {
            Proj r;
            Project(x, z, ref hint, out r);
            int i = r.i; float t = r.t;
            float hw = Lerp(halfW, i, t), bk = Lerp(bank, i, t);
            float d = r.d, a = d < 0f ? -d : d;
            int side = d >= 0f ? 1 : 0;
            float kw = Lerp(kerb[side], i, t), aw = Lerp(apron[side], i, t), tw = Lerp(trap[side], i, t);
            g = new GroundSample();
            g.vertCurv = Lerp(vcurv, i, t);
            g.bank = bk;
            g.y = SurfaceY(i, t, d);
            if (a <= hw) { g.surface = SurfaceKind.Asphalt; g.grip = 1f; g.rolling = 0.015f; g.rough = 0f; }
            else if (a <= hw + kw) { g.surface = SurfaceKind.Kerb; g.grip = 0.93f; g.rolling = 0.02f; g.rough = 0.55f; }
            else if (a <= hw + kw + aw) { g.surface = SurfaceKind.Apron; g.grip = 0.95f; g.rolling = 0.02f; g.rough = 0.05f; }
            else if (a <= hw + kw + aw + tw) { g.surface = SurfaceKind.Gravel; g.grip = 0.36f; g.rolling = 0.22f; g.rough = 0.6f; }
            else { g.surface = SurfaceKind.Grass; g.grip = 0.42f; g.rolling = 0.07f; g.rough = 0.25f; }
            if (wet > 0f)
            {
                g.grip *= 1f - WetLoss[(int)g.surface] * wet;
                if (g.surface == SurfaceKind.Grass) g.rolling += 0.03f * wet;           // mud
                else if (g.surface == SurfaceKind.Asphalt) g.rolling += 0.003f * wet;   // water on the road
            }
        }

        /// <summary>The height of the surface at a signed lateral offset d (positive right) from the point t of the way from sample i to the next. One function for the physics and for the picture of the road:
        /// the camber across the asphalt, the ground falling away outside it (3.5 %), the two centimetres of the kerb, and the gravel trap as a shallow dish (12 cm deep, with 70 cm slopes at both edges
        /// so that nothing steps).</summary>
        public float SurfaceY(int i, float t, float d)
        {
            float hw = Lerp(halfW, i, t), yc = Lerp(py, i, t), bk = Lerp(bank, i, t);
            float a = d < 0f ? -d : d;
            int side = d >= 0f ? 1 : 0;
            float kw = Lerp(kerb[side], i, t), aw = Lerp(apron[side], i, t), tw = Lerp(trap[side], i, t);
            float edge = a < hw ? a : hw;
            float y = yc - (d >= 0f ? 1f : -1f) * edge * (float)Math.Tan(bk);
            if (a <= hw) return y;
            y -= 0.035f * (a - hw);
            if (a <= hw + kw) return y + 0.02f;
            float u = a - (hw + kw + aw);
            if (u > 0f && u <= tw)
            {
                float k1 = u / 0.7f, k2 = (tw - u) / 0.7f;
                y -= 0.12f * (k1 < 1f ? k1 : 1f) * (k2 < 1f ? k2 : 1f);
            }
            return y;
        }

        public bool Barrier(float x, float z, float radius, ref int hint, out float bnx, out float bnz, out float depth)
        {
            Proj r;
            Project(x, z, ref hint, out r);
            float d = r.d, a = d < 0f ? -d : d;
            int side = d >= 0f ? 1 : 0;
            float lim = Lerp(halfW, r.i, r.t) + Lerp(run[side], r.i, r.t);
            if (a + radius <= lim) { bnx = bnz = depth = 0f; return false; }
            depth = a + radius - lim;
            float sg = d >= 0f ? -1f : 1f;
            bnx = sg * r.nx; bnz = sg * r.nz;
            return true;
        }

        /// <summary>The point at a signed distance from the centreline at sample i (positive to the right).</summary>
        public void Offset(int i, float dist, out float ox, out float oz)
        {
            i = Wrap(i);
            ox = px[i] + nx[i] * dist; oz = pz[i] + nz[i] * dist;
        }

        /// <summary>The height of the surface of the road corridor at a point (the ground's height under the bike).</summary>
        public float GroundY(float x, float z, ref int hint)
        {
            GroundSample g;
            Sample(x, z, ref hint, out g);
            return g.y;
        }

        // ------------------------------------------------------------------ the start grid
        /// <summary>The place of grid slot k (0 = pole): two columns, staggered, 8 m apart, behind the line. Heading follows the road.</summary>
        public void GridSlot(int k, out float gx, out float gz, out float gy, out float heading)
        {
            float back = 7f + 8f * (k / 2) + (k % 2 == 1 ? 4f : 0f);
            float lat = (k % 2 == 0 ? -1.8f : 1.8f);
            float sPos = WrapS(-back);
            int hint = -1;
            float f = sPos / spacing;
            int i = (int)f; float t = f - i;
            i = Wrap(i);
            float cx = Lerp(px, i, t), cz = Lerp(pz, i, t);
            float ntx = Lerp(tx, i, t), ntz = Lerp(tz, i, t);
            float l = (float)Math.Sqrt(ntx * ntx + ntz * ntz);
            ntx /= l; ntz /= l;
            gx = cx + ntz * lat; gz = cz - ntx * lat;
            gy = Lerp(py, i, t);
            heading = (float)Math.Atan2(ntx, ntz);
            if (hint < 0) hint = 0;
        }

        /// <summary>A point on the centreline at a distance along the lap, with the heading of the road.</summary>
        public void At(float sAlong, out float cx, out float cz, out float cy, out float heading)
        {
            float f = WrapS(sAlong) / spacing;
            int i = Wrap((int)f); float t = f - (int)f;
            cx = Lerp(px, i, t); cz = Lerp(pz, i, t); cy = Lerp(py, i, t);
            heading = (float)Math.Atan2(Lerp(tx, i, t), Lerp(tz, i, t));
        }
    }
}
