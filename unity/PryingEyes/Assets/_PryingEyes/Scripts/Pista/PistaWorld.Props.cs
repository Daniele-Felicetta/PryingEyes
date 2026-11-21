using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The things around the road: the starting gantry with its five lights, the pit building and the grandstand by the start straight, braking boards before the hardest corners, and the trees in belts
    /// along the track. Every structure is checked against the rest of the circuit (a point too near another stretch of road is skipped), so nothing stands on the asphalt of a different section.</summary>
    public sealed partial class PistaWorld
    {
        /// <summary>The point is clear of every guard rail by at least 'margin' metres, and the road that is nearest is the stretch around lap distance 'near' (give a negative 'near' to accept any).</summary>
        bool Clear(float x, float z, float margin, float near = -1f, float window = 220f)
        {
            Proj p;
            if (!c.ProjectNear(x, z, out p)) return true;
            int side = p.d >= 0f ? 1 : 0;
            float lim = c.Lerp(c.halfW, p.i, p.t) + c.Lerp(c.run[side], p.i, p.t);
            if (Mathf.Abs(p.d) < lim + margin) return false;
            if (near >= 0f && Mathf.Abs(c.DeltaS(near, p.s)) > window) return false;
            return true;
        }

        Vector3 OnRoad(float s, float lateral)
        {
            float f = c.WrapS(s) / c.spacing;
            int i = c.Wrap((int)f); float t = f - (int)f;
            float cx = c.Lerp(c.px, i, t), cz = c.Lerp(c.pz, i, t);
            float nxv = c.Lerp(c.nx, i, t), nzv = c.Lerp(c.nz, i, t);
            float l = Mathf.Sqrt(nxv * nxv + nzv * nzv);
            nxv /= l; nzv /= l;
            return new Vector3(cx + nxv * lateral, c.SurfaceY(i, t, lateral), cz + nzv * lateral);
        }

        float HeadingAt(float s)
        {
            float cx, cz, cy, h;
            c.At(s, out cx, out cz, out cy, out h);
            return h;
        }

        float LimitAt(float s, int side)
        {
            float f = c.WrapS(s) / c.spacing;
            int i = c.Wrap((int)f); float t = f - (int)f;
            return c.Lerp(c.halfW, i, t) + c.Lerp(c.run[side], i, t);
        }

        void BuildProps()
        {
            var steel = new MB(8);       // 0 concrete, 1 roof, 2 glass, 3 steel, 4 black, 5 seats red, 6 seats blue, 7 seats yellow
            var mats = new[] { mConcrete, mRoof, mGlass, mSteel, mBlack, mSeatRed, mSeatBlue, mSeatYellow };
            var white = new MB(2);       // boards: 0 red, 1 white
            var wmats = new[] { mSignRed, mSignWhite };

            // ---- the gantry over the start line: two pillars, a beam, and a panel of five lights facing the grid (back along the road)
            {
                float hw = c.halfW[0];
                float lim = Mathf.Max(hw + 1.8f, 0f);
                float h0 = HeadingAt(0f);
                Vector3 f = new Vector3(Mathf.Sin(h0), 0f, Mathf.Cos(h0)), r = new Vector3(f.z, 0f, -f.x);
                Vector3 mid = OnRoad(0f, 0f);
                GantryPos = mid + Vector3.up * 5.6f;
                Vector3 lpost = OnRoad(0f, -lim), rpost = OnRoad(0f, lim);
                steel.Box(3, lpost + Vector3.up * 3.3f, r, Vector3.up, f, new Vector3(0.22f, 3.4f, 0.22f), false);
                steel.Box(3, rpost + Vector3.up * 3.3f, r, Vector3.up, f, new Vector3(0.22f, 3.4f, 0.22f), false);
                Vector3 beam = mid + Vector3.up * 6.35f;
                steel.Box(3, beam, r, Vector3.up, f, new Vector3(lim + 0.3f, 0.45f, 0.35f));
                steel.Box(4, beam - f * 0.40f, r, Vector3.up, f, new Vector3(3.2f, 0.60f, 0.10f));          // the panel of the lights, on the side of the grid
                // a banner on the other face, red and white blocks
                for (int k = 0; k < 14; k++)
                {
                    float off = -lim + 0.4f + k * ((2f * lim - 0.8f) / 13f);
                    white.Box(k % 2, beam + f * 0.38f + r * off, r, Vector3.up, f, new Vector3((2f * lim - 0.8f) / 26f, 0.38f, 0.04f));
                }
                // the lamps: five dark boxes (the director lights them one by one through lampMats)
                for (int k = 0; k < 5; k++)
                {
                    var lamp = new MB(1);
                    Vector3 at = beam - f * 0.50f + r * ((k - 2) * 0.62f);
                    lamp.Box(0, at, r, Vector3.up, f, new Vector3(0.22f, 0.22f, 0.07f));
                    var lm = lamp.ToMesh("Luce");
                    made.Add(lm);
                    MB.Spawn("Luce " + k, root, lm, new[] { lampMats[k] }, false, false);
                }
                // a second row of lamps for the cameras seen from far away
                // the pit wall on the left of the start straight: a low concrete wall with a painted band
                for (float s = -120f; s < 140f; s += 6f)
                {
                    float hh = HeadingAt(s);
                    Vector3 ff = new Vector3(Mathf.Sin(hh), 0f, Mathf.Cos(hh)), rr = new Vector3(ff.z, 0f, -ff.x);
                    float l = LimitAt(s, 0) + 1.2f;
                    Vector3 p = OnRoad(s + 3f, -l);
                    if (!Clear(p.x, p.z, -2f, s + 3f, 60f)) continue;
                    steel.Box(0, p + Vector3.up * 0.55f, rr, Vector3.up, ff, new Vector3(0.28f, 0.55f, 3.05f));
                    white.Box(0, p + Vector3.up * 0.45f + rr * 0.29f, rr, Vector3.up, ff, new Vector3(0.015f, 0.12f, 3.05f));
                }
            }

            // ---- the pit building behind the pit wall: a long low block with a glazed front and a flat roof, and the pit boxes
            {
                bool ok = true;
                float s0 = -150f, s1 = 130f;
                float hh = HeadingAt((s0 + s1) * 0.5f);
                float off = LimitAt((s0 + s1) * 0.5f, 0) + 14f;
                Vector3 a = OnRoad(s0, -off), b = OnRoad(s1, -off);
                for (float s = s0; s <= s1; s += 20f) { Vector3 p = OnRoad(s, -(LimitAt(s, 0) + 14f)); if (!Clear(p.x, p.z, 4f, s, 120f)) ok = false; Vector3 q = OnRoad(s, -(LimitAt(s, 0) + 30f)); if (!Clear(q.x, q.z, 4f, s, 120f)) ok = false; }
                if (ok)
                {
                    for (float s = s0; s < s1; s += 12f)
                    {
                        float h2 = HeadingAt(s + 6f);
                        Vector3 f2 = new Vector3(Mathf.Sin(h2), 0f, Mathf.Cos(h2)), r2 = new Vector3(f2.z, 0f, -f2.x);
                        Vector3 p = OnRoad(s + 6f, -(LimitAt(s + 6f, 0) + 14f + 6f));
                        float ground = p.y;
                        steel.Box(0, p + Vector3.up * 3.2f, r2, Vector3.up, f2, new Vector3(6.0f, 3.2f, 6.1f));                      // the block
                        steel.Box(2, p + Vector3.up * 2.4f + r2 * 6.02f, r2, Vector3.up, f2, new Vector3(0.05f, 1.3f, 5.2f));        // the glazing, on the road side (r2 points right: the road is on +r)
                        steel.Box(1, p + Vector3.up * 6.55f, r2, Vector3.up, f2, new Vector3(6.4f, 0.18f, 6.3f));                    // the roof
                        steel.Box(4, p + Vector3.up * 1.2f + r2 * 6.03f, r2, Vector3.up, f2, new Vector3(0.05f, 1.0f, 2.4f));        // the garage door
                        if (((int)(s / 12f) & 1) == 0) white.Box(0, p + Vector3.up * 5.4f + r2 * 6.05f, r2, Vector3.up, f2, new Vector3(0.04f, 0.4f, 4.0f));
                    }
                }
                _ = hh; _ = a; _ = b;
            }

            // ---- the grandstand across the road: stepped terraces with seats in blocks of colour, a roof on steel posts
            {
                bool ok = true;
                float s0 = -70f, s1 = 150f;
                for (float s = s0; s <= s1; s += 20f)
                    for (float k = 0f; k <= 1f; k += 0.5f)
                    {
                        float l = LimitAt(s, 1) + 12f + k * 22f;
                        Vector3 p = OnRoad(s, l);
                        if (!Clear(p.x, p.z, 3f, s, 120f)) ok = false;
                    }
                if (ok)
                {
                    int blocks = Mathf.FloorToInt((s1 - s0) / 11f);
                    for (int q = 0; q < blocks; q++)
                    {
                        float s = s0 + q * 11f + 5.5f;
                        float h2 = HeadingAt(s);
                        Vector3 f2 = new Vector3(Mathf.Sin(h2), 0f, Mathf.Cos(h2)), r2 = new Vector3(f2.z, 0f, -f2.x);
                        float l0 = LimitAt(s, 1) + 12f;
                        Vector3 baseP = OnRoad(s, l0);
                        int seatSub = 5 + ((q + (q >> 2)) % 3);
                        for (int tier = 0; tier < 7; tier++)
                        {
                            float back = tier * 1.5f, height = 0.5f + tier * 0.8f;
                            Vector3 p = baseP + r2 * (back + 0.75f);
                            steel.Box(0, p + Vector3.up * (height * 0.5f - 0.05f), r2, Vector3.up, f2, new Vector3(0.75f, height * 0.5f + 0.05f, 5.4f), false);
                            steel.Box(seatSub, p - r2 * 0.1f + Vector3.up * (height + 0.15f), r2, Vector3.up, f2, new Vector3(0.28f, 0.15f, 5.2f));
                        }
                        // roof and posts
                        for (int pi = 0; pi < 2; pi++)
                            steel.Box(3, baseP + r2 * (pi == 0 ? 1.5f : 9.0f) + f2 * 5.2f + Vector3.up * 4.6f, r2, Vector3.up, f2, new Vector3(0.10f, 4.6f, 0.10f), false);
                        steel.Box(1, baseP + r2 * 5.4f + Vector3.up * 9.3f, r2, Vector3.up, f2, new Vector3(5.6f, 0.14f, 5.5f));
                    }
                }
            }

            // ---- braking boards: 200, 150 and 100 m before the five sharpest corners, on the outer side
            {
                var picks = new List<int>();
                for (int i = 0; i < c.n; i += 4)
                {
                    if (Mathf.Abs(c.curv[i]) < 1f / 90f) continue;
                    bool corner = true;
                    for (int j = -40; j < 0; j++) if (Mathf.Abs(c.curv[c.Wrap(i + j)]) > 1f / 200f && j > -30) { corner = false; break; }
                    if (!corner) continue;
                    bool far = true;
                    foreach (int q in picks) if (Mathf.Abs(c.DeltaS(q * c.spacing, i * c.spacing)) < 250f) far = false;
                    if (far) picks.Add(i);
                    if (picks.Count >= 8) break;
                }
                foreach (int i in picks)
                {
                    int side = c.curv[i] > 0f ? 0 : 1;       // a right turn: the board is on the left (outside of the braking line)
                    float sgn = side == 1 ? 1f : -1f;
                    for (int k = 0; k < 3; k++)
                    {
                        float s = i * c.spacing - (200f - 50f * k);
                        float h2 = HeadingAt(s);
                        Vector3 f2 = new Vector3(Mathf.Sin(h2), 0f, Mathf.Cos(h2)), r2 = new Vector3(f2.z, 0f, -f2.x);
                        float l = LimitAt(s, side) + 0.9f;
                        Vector3 p = OnRoad(s, sgn * l);
                        if (!Clear(p.x, p.z, -0.5f)) continue;
                        steel.Box(3, p + Vector3.up * 0.9f, r2, Vector3.up, f2, new Vector3(0.04f, 0.9f, 0.04f), false);
                        Vector3 face = p + Vector3.up * 1.75f - r2 * sgn * 0.0f;
                        white.Box(1, face, r2, Vector3.up, f2, new Vector3(0.05f, 0.42f, 0.55f));
                        for (int bars = 0; bars <= 2 - k; bars++) white.Box(0, face - r2 * sgn * 0.06f + f2 * (-0.30f + bars * 0.30f), r2, Vector3.up, f2, new Vector3(0.012f, 0.40f, 0.06f));
                    }
                }
            }

            Material[] used;
            var mesh = MB.Compact(steel, "Strutture", mats, out used);
            made.Add(mesh);
            MB.Spawn("Strutture", root, mesh, used, true, true);
            Material[] wused;
            var wmesh = MB.Compact(white, "Insegne", wmats, out wused);
            made.Add(wmesh);
            MB.Spawn("Insegne", root, wmesh, wused, true, true);
        }

        // ------------------------------------------------------------------ trees, in belts along the road and in clumps
        // A tree is a trunk with a few limbs (the Lit material) and cards cut out by a texture (PistaFoliage): clusters of leaves laid in a crown, or tiers of fir fronds around the stem. The mesh normals of the
        // cards are those of the crown, not of the flat card, so a tree is lit like a ball of leaves; the mesh colour carries the tint of the tree and how dark it is inside the crown (rgb) and how much the
        // card sways in the wind (alpha). All the trees of a block of 240 m are one mesh.
        IEnumerator BuildTrees()
        {
            var rng = new System.Random(seed * 7 + 3);
            const float cell = 15f;
            int cx0 = Mathf.FloorToInt(tx0 / cell), cx1 = Mathf.CeilToInt(tx1 / cell), cz0 = Mathf.FloorToInt(tz0 / cell), cz1 = Mathf.CeilToInt(tz1 / cell);
            const int block = 16;            // grid cells per mesh (240 m)
            var mats = new[] { mTrunk, mLeaf, mNeedle };
            int made0 = 0;
            for (int bz = cz0; bz < cz1; bz += block)
                for (int bx = cx0; bx < cx1; bx += block)
                {
                    var b = new MB(3).UseColors();
                    int count = 0;
                    for (int gz = bz; gz < Mathf.Min(bz + block, cz1); gz++)
                        for (int gx = bx; gx < Mathf.Min(bx + block, cx1); gx++)
                        {
                            float x = (gx + 0.15f + 0.7f * (float)rng.NextDouble()) * cell, z = (gz + 0.15f + 0.7f * (float)rng.NextDouble()) * cell;
                            Proj p;
                            if (!c.ProjectNear(x, z, out p)) continue;
                            int side = p.d >= 0f ? 1 : 0;
                            float lim = c.Lerp(c.halfW, p.i, p.t) + c.Lerp(c.run[side], p.i, p.t);
                            float e = Mathf.Abs(p.d) - lim;
                            if (e < 11f || e > 150f) continue;
                            // belts and clearings: a slow noise says where the woods are
                            float wood = PistaTex.Fbm(Mathf.Repeat(x / 3000f, 1f), Mathf.Repeat(z / 3000f, 1f), 9, 3, seed + 5);
                            float density = Mathf.Clamp01((wood - 0.40f) * 4.5f) * (1f - 0.5f * S(60f, 150f, e));
                            if ((float)rng.NextDouble() > density) continue;
                            float y = TerrainY(x, z) - 0.25f;
                            AddTree(b, new Vector3(x, y, z), rng);
                            count++;
                        }
                    if (count > 0)
                    {
                        Material[] used;
                        var mesh = MB.Compact(b, "Alberi", mats, out used);
                        made.Add(mesh);
                        MB.Spawn("Alberi " + made0++, root, mesh, used, true, true);          // the trees throw their shadows across the road (the sky gives the shadows three times the usual reach)
                    }
                    Progress = 0.86f + 0.12f * ((bz - cz0) / (float)(cz1 - cz0));
                    yield return null;
                }
        }

        static float Rf(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

        static byte B8(float v) { return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255); }

        void AddTree(MB b, Vector3 baseP, System.Random rng)
        {
            if (rng.NextDouble() < 0.35) AddFir(b, baseP, rng); else AddBroadleaf(b, baseP, rng);
        }

        /// <summary>A broadleaf: a leaning trunk, a few limbs into the crown, and a crown of 24 to 31 cards of leaves laid in a squashed ball, the inner ones darker.</summary>
        void AddBroadleaf(MB b, Vector3 baseP, System.Random rng)
        {
            float h = Rf(rng, 7.5f, 14.5f);
            float trunkH = h * Rf(rng, 0.36f, 0.48f);
            float crownR = h * Rf(rng, 0.30f, 0.40f);
            float lean = Rf(rng, 0f, 0.07f), leanA = Rf(rng, 0f, 6.2832f);
            Vector3 top = baseP + new Vector3(Mathf.Cos(leanA) * lean * trunkH, trunkH, Mathf.Sin(leanA) * lean * trunkH);
            Limb(b, 0, baseP - Vector3.up * 0.3f, top, 0.20f + h * 0.012f, 0.10f + h * 0.006f, 7);
            Vector3 centre = top + Vector3.up * crownR * Rf(rng, 0.45f, 0.65f);
            float ry = crownR * Rf(rng, 0.78f, 0.98f), yScale = crownR / ry;
            // the colour of this tree: a green between yellowish and bluish, a little lighter or darker
            float hue = Rf(rng, 0f, 1f), tone = Rf(rng, 0.80f, 1.0f);
            float tr = Mathf.Lerp(0.70f, 1.0f, hue) * tone, tg = Mathf.Lerp(1.0f, 0.92f, hue) * tone, tb = Mathf.Lerp(0.95f, 0.60f, hue) * tone;
            int cards = 24 + rng.Next(8);
            for (int k = 0; k < cards; k++)
            {
                float u = Rf(rng, -0.25f, 1f), phi = Rf(rng, 0f, 6.2832f), sr = Mathf.Sqrt(1f - u * u);
                Vector3 dir = new Vector3(sr * Mathf.Cos(phi), u, sr * Mathf.Sin(phi));
                float dist = Mathf.Pow(Rf(rng, 0.10f, 1f), 0.65f);                                    // 0 at the middle, 1 on the surface
                Vector3 pc = centre + new Vector3(dir.x * crownR * dist, dir.y * ry * dist, dir.z * crownR * dist);
                float size = crownR * Rf(rng, 0.50f, 0.78f);
                Vector3 fn = (dir + new Vector3(Rf(rng, -0.5f, 0.5f), Rf(rng, -0.4f, 0.6f), Rf(rng, -0.5f, 0.5f))).normalized;
                Vector3 right = Vector3.Cross(fn, Vector3.up);
                if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
                right.Normalize();
                Vector3 upv = Vector3.Cross(right, fn);
                float roll = Rf(rng, 0f, 6.2832f), cs = Mathf.Cos(roll), sn = Mathf.Sin(roll);
                Vector3 r2 = (right * cs + upv * sn) * size, u2 = (-right * sn + upv * cs) * size;
                float ao = Mathf.Lerp(0.55f, 1.0f, dist);
                float wind = Mathf.Clamp01(0.25f + 0.75f * dist) * Mathf.Clamp01(0.5f + (pc.y - baseP.y) / h * 0.5f);
                var col = new Color32(B8(tr * ao), B8(tg * ao), B8(tb * ao), B8(wind));
                CrownCard(b, 1, pc, r2, u2, centre, yScale, col);
                // a limb into a few of the clusters, seen through the gaps of the leaves
                if (k < 4) Limb(b, 0, top, centre + (pc - centre) * 0.55f, 0.07f + h * 0.004f, 0.025f, 5);
            }
        }

        /// <summary>A fir: a stem with nine tiers of fronds (ten cards around the bottom one, six around the top), each card a trough that droops from the stem to its tip so that it has a body seen from the side,
        /// and a leader of three upright cards that ends the tree in a point.</summary>
        void AddFir(MB b, Vector3 baseP, System.Random rng)
        {
            float h = Rf(rng, 9f, 17f);
            float rBase = h * Rf(rng, 0.17f, 0.23f);
            Limb(b, 0, baseP - Vector3.up * 0.3f, baseP + Vector3.up * h * 0.90f, 0.17f + h * 0.010f, 0.05f, 6);
            float tone = Rf(rng, 0.80f, 1.0f), hue = Rf(rng, 0f, 1f);
            float tr = Mathf.Lerp(0.75f, 0.95f, hue) * tone, tg = Mathf.Lerp(0.95f, 1.0f, hue) * tone, tb = Mathf.Lerp(0.95f, 0.75f, hue) * tone;
            const int tiers = 9;
            float yaw0 = Rf(rng, 0f, 6.2832f);
            for (int t = 0; t < tiers; t++)
            {
                float f = t / (float)(tiers - 1);                                                     // 0 low .. 1 at the top
                float y = h * (0.10f + 0.72f * f);
                float reach = rBase * Mathf.Pow(1f - f, 0.9f) + 0.6f;                                 // how far the fronds of this tier go out
                int n = 10 - t / 2;
                float ao = Mathf.Lerp(0.74f, 1.0f, f);
                for (int k = 0; k < n; k++)
                {
                    float yaw = yaw0 + t * 0.9f + k * 6.2832f / n + Rf(rng, -0.2f, 0.2f);
                    Vector3 dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)), side = new Vector3(-dir.z, 0f, dir.x);
                    float len = reach * Rf(rng, 1.05f, 1.3f), wid = Mathf.Max(0.5f, reach * 0.58f);
                    Vector3 inner = baseP + Vector3.up * y + dir * 0.15f;
                    Vector3 tip = inner + dir * len + Vector3.down * (len * Rf(rng, 0.45f, 0.75f));
                    Vector3 nrm = (dir + Vector3.up * 0.55f).normalized;
                    var cIn = new Color32(B8(tr * ao), B8(tg * ao), B8(tb * ao), B8(0.05f));
                    var cTip = new Color32(B8(tr * ao), B8(tg * ao), B8(tb * ao), B8(0.55f * (0.4f + 0.6f * f)));
                    // a trough: the middle line lower than the edges
                    Vector3 sag = Vector3.down * (wid * 0.30f);
                    int i = b.VertC(inner - side * wid, nrm, new Vector2(0f, 1f), cIn);
                    b.VertC(inner + sag, nrm, new Vector2(0.5f, 1f), cIn);
                    b.VertC(inner + side * wid, nrm, new Vector2(1f, 1f), cIn);
                    b.VertC(tip - side * wid * 0.9f, nrm, new Vector2(0f, 0f), cTip);
                    b.VertC(tip + sag, nrm, new Vector2(0.5f, 0f), cTip);
                    b.VertC(tip + side * wid * 0.9f, nrm, new Vector2(1f, 0f), cTip);
                    b.Quad(2, i, i + 1, i + 4, i + 3);
                    b.Quad(2, i + 1, i + 2, i + 5, i + 4);
                }
            }
            // the leader: three upright cards, the fronds of the top turned to the sky
            for (int k = 0; k < 3; k++)
            {
                float yaw = yaw0 + k * 2.0944f;
                Vector3 dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)), side = new Vector3(-dir.z, 0f, dir.x);
                Vector3 inner = baseP + Vector3.up * h * 0.86f, tip = baseP + Vector3.up * h;
                float wid = 0.42f;
                Vector3 nrm = (dir * 0.9f + Vector3.up * 0.5f).normalized;
                var cIn = new Color32(B8(tr * 0.9f), B8(tg * 0.9f), B8(tb * 0.9f), B8(0.05f));
                var cTip = new Color32(B8(tr * 0.9f), B8(tg * 0.9f), B8(tb * 0.9f), B8(0.30f));
                int i = b.VertC(inner - side * wid, nrm, new Vector2(0f, 1f), cIn);
                b.VertC(inner + side * wid, nrm, new Vector2(1f, 1f), cIn);
                b.VertC(tip + side * wid * 0.25f, nrm, new Vector2(1f, 0f), cTip);
                b.VertC(tip - side * wid * 0.25f, nrm, new Vector2(0f, 0f), cTip);
                b.Quad(2, i, i + 1, i + 2, i + 3);
            }
        }

        /// <summary>A card of leaves centred at 'c' with half-axes 'r' and 'u'; each corner takes the normal of the crown there (away from its middle, the vertical axis stretched back by 'yScale', and a little
        /// toward the sky), so the light on a tree runs smoothly over it and not card by card.</summary>
        static void CrownCard(MB b, int sub, Vector3 c, Vector3 r, Vector3 u, Vector3 crownC, float yScale, Color32 col)
        {
            Vector3 p0 = c - r - u, p1 = c + r - u, p2 = c + r + u, p3 = c - r + u;
            int i = b.VertC(p0, CrownNormal(p0, crownC, yScale), new Vector2(0f, 0f), col);
            b.VertC(p1, CrownNormal(p1, crownC, yScale), new Vector2(1f, 0f), col);
            b.VertC(p2, CrownNormal(p2, crownC, yScale), new Vector2(1f, 1f), col);
            b.VertC(p3, CrownNormal(p3, crownC, yScale), new Vector2(0f, 1f), col);
            b.Quad(sub, i, i + 1, i + 2, i + 3);
        }

        static Vector3 CrownNormal(Vector3 p, Vector3 crownC, float yScale)
        {
            Vector3 d = p - crownC;
            d.y *= yScale;
            return (d.normalized + Vector3.up * 0.15f).normalized;
        }

        /// <summary>A tapered cylinder from a to c (radius r0 at a, r1 at c) with 'seg' sides: a trunk or a limb. The winding is checked once so that the outside is the front whatever the direction.</summary>
        static void Limb(MB b, int sub, Vector3 a, Vector3 c, float r0, float r1, int seg)
        {
            Vector3 axis = c - a;
            float len = axis.magnitude;
            if (len < 1e-4f) return;
            axis /= len;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.95f ? Vector3.right : Vector3.up).normalized, v = Vector3.Cross(axis, u);
            int i0 = b.Count;
            var white = new Color32(255, 255, 255, 0);
            for (int j = 0; j <= seg; j++)
            {
                float ang = j * Mathf.PI * 2f / seg;
                Vector3 radial = u * Mathf.Cos(ang) + v * Mathf.Sin(ang);
                b.VertC(a + radial * r0, radial, new Vector2(j / (float)seg, 0f), white);
                b.VertC(c + radial * r1, radial, new Vector2(j / (float)seg, 1f), white);
            }
            bool flip = Vector3.Dot(Vector3.Cross(b.v[i0 + 1] - b.v[i0], b.v[i0 + 3] - b.v[i0]), b.n[i0]) < 0f;
            for (int j = 0; j < seg; j++)
            {
                int k = i0 + j * 2;
                if (!flip) { b.Tri(sub, k, k + 1, k + 3); b.Tri(sub, k, k + 3, k + 2); }
                else { b.Tri(sub, k, k + 3, k + 1); b.Tri(sub, k, k + 2, k + 3); }
            }
        }
    }
}
