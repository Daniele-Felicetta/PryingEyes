using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The boards and the spectators of the trackside: advertising boards on the pit wall and on posts at the outside of the sharpest corners, spectators on the tiers of the grandstand, and small
    /// stands of their own at the outside of a few corners. The pictures are those of PistaSigns; without them (or without the shader of the leaves, which cuts the crowd out) none of this is built.</summary>
    public sealed partial class PistaWorld
    {
        Material mBoards, mCrowd;

        /// <summary>How much trackside was built (for the test, which checks that the boards and the spectators are really there, and for the log).</summary>
        public int BoardCount, CrowdCards, BillboardCount, CornerStandCount, TyreWalls, TyreCount, MarshalPosts;
        public bool CrowdShown;                                            // the crowd is in the scene (it needs the cut-out shader: without it the cards are counted but not shown)

        /// <summary>A place to stand and a thing to look at: the photographs of -pistaworldshots take one picture of every kind of trackside from here, as a rider would see it.</summary>
        public struct Vantage { public string kind; public Vector3 from, look; public float fov; }
        public readonly List<Vantage> vantages = new List<Vantage>();

        /// <summary>A board of the atlas on a wall or on posts: a quad centred at 'centre' with the unit vectors 'along' and 'up', half sizes 'hw' and 'hh', facing 'nrm', showing one design. For the text to read
        /// the right way round from the front, 'along' must be the cross product of 'nrm' and 'up'.</summary>
        void BoardQuad(MB b, int sub, Vector3 centre, Vector3 along, Vector3 up, float hw, float hh, Vector3 nrm, int design)
        {
            BoardCount++;
            Vector4 r = PistaSigns.Rect(design);
            int i = b.Vert(centre - along * hw - up * hh, nrm, new Vector2(r.x, r.y));
            b.Vert(centre + along * hw - up * hh, nrm, new Vector2(r.z, r.y));
            b.Vert(centre + along * hw + up * hh, nrm, new Vector2(r.z, r.w));
            b.Vert(centre - along * hw + up * hh, nrm, new Vector2(r.x, r.w));
            if (Vector3.Dot(Vector3.Cross(b.v[i + 1] - b.v[i], b.v[i + 2] - b.v[i]), nrm) >= 0f) b.Quad(sub, i, i + 1, i + 2, i + 3); else b.Quad(sub, i, i + 3, i + 2, i + 1);
        }

        /// <summary>A card of spectators standing on a tier at 'foot': 'halfW' to each side along 'along', 'h' high, facing 'facing', two tiles of the crowd picture across starting at 'u0'. The vertex alpha is the
        /// share of the wind's sway the card takes (a hint, so that the crowd is never quite still).</summary>
        void CrowdCard(MB b, Vector3 foot, Vector3 along, Vector3 facing, float halfW, float h, float u0)
        {
            CrowdCards++;
            Vector3 nrm = (facing + Vector3.up * 0.25f).normalized;
            var col = new Color32(255, 255, 255, 14);
            float u1 = u0 + halfW * 2f / 2.6f;                          // one tile of the picture is 2.6 m of card
            int i = b.VertC(foot - along * halfW, nrm, new Vector2(u0, 0f), col);
            b.VertC(foot + along * halfW, nrm, new Vector2(u1, 0f), col);
            b.VertC(foot + along * halfW + Vector3.up * h, nrm, new Vector2(u1, 1f), col);
            b.VertC(foot - along * halfW + Vector3.up * h, nrm, new Vector2(u0, 1f), col);
            b.Quad(0, i, i + 1, i + 2, i + 3);
        }

        /// <summary>The apexes of the sharpest corners (the local maxima of the curvature), at least 'gap' metres apart, up to 'max' of them: sample indices.</summary>
        List<int> SharpCorners(float minCurv, float gap, int max)
        {
            var picks = new List<int>();
            for (int i = 0; i < c.n; i += 3)
            {
                float k = Mathf.Abs(c.curv[i]);
                if (k < minCurv) continue;
                bool top = true;
                for (int j = -10; j <= 10 && top; j++) if (Mathf.Abs(c.curv[c.Wrap(i + j)]) > k + 1e-6f) top = false;
                if (!top) continue;
                bool far = true;
                foreach (int q in picks) if (Mathf.Abs(c.DeltaS(q * c.spacing, i * c.spacing)) < gap) far = false;
                if (far) picks.Add(i);
                if (picks.Count >= max) break;
            }
            return picks;
        }

        /// <summary>Boards on posts at the outside of the sharpest corners, a little before the apex, turned back along the road and in toward it so that a bike coming to the corner reads them. Each is
        /// 6.4 m by 2.4 m with a steel back and two posts.</summary>
        void BuildBillboards(MB ads, MB frame, int frameSub)
        {
            int n = 0;
            foreach (int i in SharpCorners(1f / 130f, 300f, 7))
            {
                int side = c.curv[i] > 0f ? 0 : 1;                      // the outside of the corner
                float sgn = side == 1 ? 1f : -1f;
                float s = i * c.spacing - 35f;
                float h2 = HeadingAt(s);
                Vector3 f2 = new Vector3(Mathf.Sin(h2), 0f, Mathf.Cos(h2)), r2 = new Vector3(f2.z, 0f, -f2.x);
                Vector3 p = OnRoad(s, sgn * (LimitAt(s, side) + 5.0f));
                if (!Clear(p.x, p.z, 3f)) continue;
                float gy = TerrainY(p.x, p.z);
                Vector3 nrm = (-f2 - r2 * sgn).normalized;
                Vector3 along = Vector3.Cross(nrm, Vector3.up).normalized;
                Vector3 centre = new Vector3(p.x, gy + 3.1f, p.z);
                BoardQuad(ads, 0, centre + nrm * 0.07f, along, Vector3.up, 3.2f, 1.2f, nrm, n * 5 + 3);
                frame.Box(frameSub, centre, along, Vector3.up, nrm, new Vector3(3.3f, 1.3f, 0.06f));
                for (int k = -1; k <= 1; k += 2)
                    frame.Box(frameSub, new Vector3(p.x, gy + 1.15f, p.z) + along * (k * 2.5f), along, Vector3.up, nrm, new Vector3(0.10f, 1.5f, 0.10f), false);
                if (n < 2) vantages.Add(new Vantage { kind = "cartellone", from = OnRoad(s - 60f, 0f) + Vector3.up * 1.5f, look = centre, fov = 45f });
                n++;
            }
            BillboardCount = n;
        }

        /// <summary>A small stand of its own at the outside of a few corners: three blocks of four tiers with spectators, facing the road, set well back of the guard rail.</summary>
        void BuildCornerStands(MB steel, MB crowd, int[] seatSubs, System.Random rng)
        {
            int made0 = 0;
            foreach (int i in SharpCorners(1f / 150f, 700f, 5))
            {
                int side = c.curv[i] > 0f ? 0 : 1;
                float sgn = side == 1 ? 1f : -1f;
                float sMid = i * c.spacing;
                bool ok = true;
                for (float s = sMid - 24f; s <= sMid + 24f && ok; s += 8f)
                    for (float k = 0f; k <= 1f; k += 0.5f)
                    {
                        Vector3 p = OnRoad(s, sgn * (LimitAt(s, side) + 9f + k * 8f));
                        if (!Clear(p.x, p.z, 3f)) ok = false;
                    }
                if (!ok) continue;
                for (int q = -1; q <= 1; q++)
                {
                    float s = sMid + q * 11f;
                    float h2 = HeadingAt(s);
                    Vector3 f2 = new Vector3(Mathf.Sin(h2), 0f, Mathf.Cos(h2)), r2 = new Vector3(f2.z, 0f, -f2.x);
                    Vector3 back = r2 * sgn;                            // away from the road
                    Vector3 baseP = OnRoad(s, sgn * (LimitAt(s, side) + 9f));
                    baseP.y = Mathf.Min(baseP.y, TerrainY(baseP.x, baseP.z) + 0.2f);
                    int seatSub = seatSubs[(made0 + q + 1) % seatSubs.Length];
                    for (int tier = 0; tier < 4; tier++)
                    {
                        float bk = tier * 1.5f, height = 0.5f + tier * 0.8f;
                        Vector3 p = baseP + back * (bk + 0.75f);
                        steel.Box(0, p + Vector3.up * (height * 0.5f - 0.05f), back, Vector3.up, f2, new Vector3(0.75f, height * 0.5f + 0.05f, 5.4f), false);
                        steel.Box(seatSub, p - back * 0.1f + Vector3.up * (height + 0.15f), back, Vector3.up, f2, new Vector3(0.28f, 0.15f, 5.2f));
                        if (crowd != null) CrowdCard(crowd, p - back * 0.12f + Vector3.up * (height + 0.30f), f2, -back, 5.2f, 1.25f, (float)rng.NextDouble() * 4f);
                    }
                }
                if (made0 < 2) vantages.Add(new Vantage { kind = "tribunetta", from = OnRoad(sMid - 30f, 0f) + Vector3.up * 1.6f, look = OnRoad(sMid, sgn * (LimitAt(sMid, side) + 13f)) + Vector3.up * 2.4f, fov = 55f });
                made0++;
            }
            CornerStandCount = made0;
        }

        /// <summary>A triangle that faces 'nrm' whichever way its corners were given.</summary>
        static void TriFacing(MB m, int sub, int a, int b, int c2, Vector3 nrm)
        {
            if (Vector3.Dot(Vector3.Cross(m.v[b] - m.v[a], m.v[c2] - m.v[a]), nrm) >= 0f) m.Tri(sub, a, b, c2); else m.Tri(sub, a, c2, b);
        }

        /// <summary>A tyre lying flat: a seven-sided prism of radius 'r' and height 'h' standing on 'foot', shaded smooth round its sides, with a fan on top when it is the top of a stack (a tyre under another
        /// is never seen from above, and neither is its bottom).</summary>
        static void Tyre(MB m, int sub, Vector3 foot, float r, float h, bool top, float turn)
        {
            const int N = 7;
            int i0 = m.Count;
            for (int k = 0; k < N; k++)
            {
                float a = (k + turn) / N * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                m.Vert(foot + dir * r, dir, new Vector2(k / (float)N, 0f));
                m.Vert(foot + dir * r + Vector3.up * h, dir, new Vector2(k / (float)N, 1f));
            }
            for (int k = 0; k < N; k++)
            {
                int a0 = i0 + 2 * k, a1 = i0 + 2 * ((k + 1) % N);
                Vector3 mid = (m.v[a0] + m.v[a1]) * 0.5f - foot; mid.y = 0f;
                TriFacing(m, sub, a0, a1, a1 + 1, mid);
                TriFacing(m, sub, a0, a1 + 1, a0 + 1, mid);
            }
            if (!top) return;
            int ct = m.Vert(foot + Vector3.up * h, Vector3.up, new Vector2(0.5f, 0.5f));
            int ring = m.Count;
            for (int k = 0; k < N; k++) m.Vert(m.v[i0 + 2 * k + 1], Vector3.up, new Vector2(0.5f, 0.5f));
            for (int k = 0; k < N; k++) TriFacing(m, sub, ct, ring + k, ring + (k + 1) % N, Vector3.up);
        }

        /// <summary>Tyre barriers: a wall of tyres lying flat, two deep and five high, just behind the guard rail on the outside of the sharpest corners; the three upper tyres of every stack are painted, in
        /// threes, white and red (the rail hides the lower ones from a rider's eye). One mesh for each wall (a wall is some seven hundred tyres).</summary>
        void BuildTyreWalls()
        {
            var mats = new[] { mTyre, mSignWhite, mSignRed };
            var rng = new System.Random(seed * 17 + 9);
            foreach (int i in SharpCorners(1f / 110f, 250f, 5))
            {
                int side = c.curv[i] > 0f ? 0 : 1;
                float sgn = side == 1 ? 1f : -1f;
                float sMid = i * c.spacing;
                var m = new MB(3);
                int column = 0, tyres = 0;
                for (float s = sMid - 24f; s <= sMid + 24f; s += 0.64f, column++)
                {
                    float h2 = HeadingAt(s);
                    Vector3 r2 = new Vector3(Mathf.Cos(h2), 0f, -Mathf.Sin(h2));
                    Vector3 back = r2 * sgn;                            // away from the road
                    for (int row = 0; row < 2; row++)
                    {
                        Vector3 p = OnRoad(s, sgn * (LimitAt(s, side) + 1.2f)) + back * (0.32f + row * 0.64f);
                        if (!Clear(p.x, p.z, 0.3f)) continue;
                        float gy = TerrainY(p.x, p.z) - 0.05f;
                        float turn = (float)rng.NextDouble();
                        int paint = ((column / 3) & 1) == 0 ? 1 : 2;
                        for (int tier = 0; tier < 5; tier++, tyres++)
                            Tyre(m, tier < 2 ? 0 : paint, new Vector3(p.x, gy + tier * 0.2f, p.z), 0.31f, 0.2f, tier == 4, turn);
                    }
                }
                if (tyres == 0) continue;
                var mesh = m.ToMesh("Gomme");
                made.Add(mesh);
                MB.Spawn("Gomme " + TyreWalls, root, mesh, mats, true, true);
                if (TyreWalls < 2) vantages.Add(new Vantage { kind = "gomme", from = OnRoad(sMid - (TyreWalls == 0 ? 45f : 18f), 0f) + Vector3.up * 1.45f, look = OnRoad(sMid, sgn * (LimitAt(sMid, side) + 1.5f)) + Vector3.up * 0.5f, fov = TyreWalls == 0 ? 50f : 55f });
                TyreWalls++;
                TyreCount += tyres;
            }
        }

        /// <summary>Marshal posts: a small concrete booth with a roof, a window and a door on the road side, and a pole with a yellow flag, a little behind the guard rail on the outside of the sharper corners.</summary>
        void BuildMarshalPosts(MB steel)
        {
            foreach (int i in SharpCorners(1f / 200f, 380f, 8))
            {
                int side = c.curv[i] > 0f ? 0 : 1;
                float sgn = side == 1 ? 1f : -1f;
                float s = i * c.spacing + 14f;
                float h2 = HeadingAt(s);
                Vector3 f2 = new Vector3(Mathf.Sin(h2), 0f, Mathf.Cos(h2)), r2 = new Vector3(f2.z, 0f, -f2.x);
                Vector3 p = OnRoad(s, sgn * (LimitAt(s, side) + 4.5f));
                if (!Clear(p.x, p.z, 3f)) continue;
                float gy = TerrainY(p.x, p.z);
                Vector3 back = r2 * sgn;                                // away from the road
                Vector3 ctr = new Vector3(p.x, gy + 1.05f, p.z);
                steel.Box(0, ctr, r2, Vector3.up, f2, new Vector3(0.95f, 1.05f, 1.1f));                                        // the booth
                steel.Box(1, ctr + Vector3.up * 1.14f, r2, Vector3.up, f2, new Vector3(1.15f, 0.07f, 1.3f));                  // its roof, a little over the walls
                Vector3 face = ctr - back * 0.96f;
                steel.Box(2, face + Vector3.up * 0.30f + f2 * 0.45f, r2, Vector3.up, f2, new Vector3(0.03f, 0.38f, 0.55f));    // the window
                steel.Box(4, face - Vector3.up * 0.20f - f2 * 0.65f, r2, Vector3.up, f2, new Vector3(0.03f, 0.85f, 0.30f));    // the door
                Vector3 pole = new Vector3(p.x, gy, p.z) + f2 * 1.55f;
                steel.Box(3, pole + Vector3.up * 1.7f, r2, Vector3.up, f2, new Vector3(0.03f, 1.7f, 0.03f), false);           // the pole
                steel.Box(7, pole + Vector3.up * 3.1f + f2 * 0.45f, r2, Vector3.up, f2, new Vector3(0.012f, 0.28f, 0.45f));    // the flag, yellow, streaming along the road
                if (MarshalPosts < 2) vantages.Add(new Vantage { kind = "commissari", from = OnRoad(s - 45f, 0f) + Vector3.up * 1.5f, look = ctr + Vector3.up * 0.4f, fov = 45f });
                MarshalPosts++;
            }
        }
    }
}
