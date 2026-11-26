using System;
using System.Collections.Generic;

namespace Production.Racing
{
    /// <summary>Makes a race circuit out of a seed: the same seed gives the same circuit on every machine, so the host only has to send a number.
    ///
    /// The plan is a list of straights and constant-radius arcs. A lap is a main straight (the start line sits on it, with room for the grid behind), then 7 to 11 corner groups of four kinds (a single
    /// corner from a kink to a hairpin, a chicane, an esse, a double apex that tightens), joined by straights. The angles are scaled so the lap turns exactly 360 degrees, and the lengths of the free
    /// straights are corrected by a weighted least-squares step so the loop closes to the millimetre. A plan that cannot close without squeezing a straight, that is too short or too long, or that
    /// passes too close to itself is thrown away and the next one is drawn; if all fail a plain two-hairpin circuit is used.
    ///
    /// The finished plan is sampled every two metres and dressed: smooth hills with a few crests and dips (flat at the grid), camber in the corners, kerbs, paved run-off, gravel traps on the outside
    /// of some corners, grass and the barrier, with the run-off squeezed where two parts of the circuit come close to each other.</summary>
    public static class CircuitGen
    {
        public const float Spacing = 2f;
        public const int MaxAttempts = 800;
        const float MinLap = 2400f, MaxLap = 6000f, MaxStraight = 700f;
        const float RoadHalfWidth = 6.5f;

        struct Seg
        {
            public float len, curv;
            public bool free;
            public float minLen;
        }

        public static Circuit Generate(int seed) { int a; return Generate(seed, out a); }

        /// <summary>The circuit for a seed, named by 'namer' (see the next overload).</summary>
        public static Circuit Generate(int seed, Func<Rng, string> namer) { int a; return Generate(seed, out a, namer); }

        /// <summary>The circuit for a seed. 'attempts' tells how many plans were drawn (one more than the limit means the plain fallback was used). 'namer' gives the circuit its name from the state of the
        /// generator after the last draw of the circuit, so the same seed gives the same name; without one the circuit is named after its seed.</summary>
        public static Circuit Generate(int seed, out int attempts, Func<Rng, string> namer = null)
        {
            var master = new Rng(seed);
            for (attempts = 1; attempts <= MaxAttempts; attempts++)
            {
                var rng = new Rng((int)master.Next());
                List<Seg> plan = Plan(ref rng);
                if (plan == null || !Close(plan)) continue;
                Circuit c = Build(plan, ref rng, seed, false, namer);
                if (c != null) return c;
            }
            attempts = MaxAttempts + 1;
            var fr = new Rng(seed);
            return Build(Fallback(), ref fr, seed, true, namer);
        }

        // ================================================================== the plan
        static List<Seg> Plan(ref Rng r)
        {
            float deg = RacingMath.Deg2Rad;
            float dir = r.Chance(0.5f) ? 1f : -1f;
            int groups = 9 + r.Int(6);
            var ang = new List<float>(); var cls = new List<int>(); var gap = new List<float>(); var groupEnd = new List<int>();
            for (int g = 0; g < groups; g++)
            {
                float roll = r.Value();
                if (roll < 0.50f)
                {
                    float sg = r.Chance(0.64f) ? dir : -dir;
                    ang.Add(sg * r.Range(40f, 160f) * deg); cls.Add(0); gap.Add(0f);
                }
                else if (roll < 0.68f)
                {
                    float sg = r.Chance(0.5f) ? dir : -dir;
                    ang.Add(sg * r.Range(32f, 62f) * deg); cls.Add(1); gap.Add(r.Range(8f, 30f));
                    ang.Add(-sg * r.Range(32f, 62f) * deg); cls.Add(1); gap.Add(0f);
                }
                else if (roll < 0.82f)
                {
                    float sg = r.Chance(0.5f) ? dir : -dir;
                    ang.Add(sg * r.Range(25f, 55f) * deg); cls.Add(2); gap.Add(0f);
                    ang.Add(-sg * r.Range(25f, 55f) * deg); cls.Add(2); gap.Add(0f);
                }
                else
                {
                    float sg = r.Chance(0.85f) ? dir : -dir;
                    ang.Add(sg * r.Range(30f, 75f) * deg); cls.Add(3); gap.Add(r.Range(0f, 15f));
                    ang.Add(sg * r.Range(40f, 90f) * deg); cls.Add(3); gap.Add(0f);
                }
                groupEnd.Add(ang.Count);
            }
            float total = 0f;
            for (int i = 0; i < ang.Count; i++) total += ang[i];
            float f = dir * RacingMath.TwoPi / (total == 0f ? 1f : total);
            if (!(f > 0.62f && f < 1.55f)) return null;

            var plan = new List<Seg>();
            plan.Add(new Seg { len = r.Range(140f, 210f), free = true, minLen = 120f });
            int longG = 1 + r.Int(groups - 2);
            int a0 = 0;
            for (int g = 0; g < groups; g++)
            {
                for (int k = a0; k < groupEnd[g]; k++)
                {
                    float a = ang[k] * f;
                    float rad = Radius(cls[k], RacingMath.Abs(a), k == a0, ref r);
                    plan.Add(new Seg { len = RacingMath.Abs(a) * rad, curv = (a > 0f ? 1f : -1f) / rad });
                    if (k < groupEnd[g] - 1 && gap[k] > 0.5f) plan.Add(new Seg { len = gap[k] });
                }
                a0 = groupEnd[g];
                bool last = g == groups - 1;
                float sl = last ? r.Range(210f, 330f) : g == longG ? r.Range(330f, 520f) : r.Range(45f, 230f);
                plan.Add(new Seg { len = sl, free = true, minLen = last ? 180f : 30f });
            }
            return plan;
        }

        static float Radius(int cls, float a, bool first, ref Rng r)
        {
            float deg = RacingMath.Deg2Rad;
            switch (cls)
            {
                case 1: return r.Range(26f, 46f);
                case 2: return r.Range(60f, 130f);
                case 3: return first ? r.Range(45f, 110f) : r.Range(24f, 60f);
                default:
                    if (a > 128f * deg) return r.Range(15f, 24f);
                    if (a > 80f * deg) return r.Range(26f, 70f);
                    if (a > 45f * deg) return r.Range(55f, 140f);
                    return r.Range(110f, 260f);
            }
        }

        /// <summary>Two hairpins and two straights: always valid.</summary>
        static List<Seg> Fallback()
        {
            var plan = new List<Seg>();
            plan.Add(new Seg { len = 160f, free = true, minLen = 120f });
            plan.Add(new Seg { len = RacingMath.Pi * 60f, curv = 1f / 60f });
            plan.Add(new Seg { len = 420f, free = true, minLen = 30f });
            plan.Add(new Seg { len = RacingMath.Pi * 60f, curv = 1f / 60f });
            plan.Add(new Seg { len = 260f, free = true, minLen = 180f });
            return plan;
        }

        /// <summary>Correct the free straights so the loop closes: a weighted least-squares step, repeated while some straight hits its minimum.</summary>
        static bool Close(List<Seg> plan)
        {
            int m = plan.Count;
            var dx = new double[m]; var dz = new double[m]; var w = new double[m];
            for (int it = 0; it < 40; it++)
            {
                double x = 0, z = 0, th = 0, sxx = 0, sxz = 0, szz = 0;
                for (int i = 0; i < m; i++)
                {
                    Seg s = plan[i];
                    w[i] = 0;
                    if (s.curv == 0f)
                    {
                        double sn = Math.Sin(th), cs = Math.Cos(th);
                        dx[i] = sn; dz[i] = cs;
                        x += sn * s.len; z += cs * s.len;
                        if (s.free)
                        {
                            w[i] = Math.Max(s.len - s.minLen, 3.0);
                            sxx += w[i] * sn * sn; sxz += w[i] * sn * cs; szz += w[i] * cs * cs;
                        }
                    }
                    else
                    {
                        double k = s.curv, th2 = th + k * s.len;
                        x += (Math.Cos(th) - Math.Cos(th2)) / k;
                        z += (Math.Sin(th2) - Math.Sin(th)) / k;
                        th = th2;
                    }
                }
                if (Math.Sqrt(x * x + z * z) < 0.01) return true;
                double det = sxx * szz - sxz * sxz;
                if (Math.Abs(det) < 1e-3) return false;
                double lx = (-x * szz + z * sxz) / det, lz = (x * sxz - z * sxx) / det;
                for (int i = 0; i < m; i++)
                {
                    if (w[i] == 0) continue;
                    Seg s = plan[i];
                    double nl = s.len + w[i] * (dx[i] * lx + dz[i] * lz);
                    s.len = (float)Math.Max(s.minLen, nl);
                    plan[i] = s;
                }
            }
            return false;
        }

        // ================================================================== the finished circuit
        static Circuit Build(List<Seg> plan, ref Rng r, int seed, bool relaxed, Func<Rng, string> namer)
        {
            double total = 0;
            for (int i = 0; i < plan.Count; i++) total += plan[i].len;
            if (!relaxed && (total < MinLap || total > MaxLap)) return null;
            if (!relaxed)
                for (int i = 0; i < plan.Count; i++)
                    if (plan[i].curv == 0f && plan[i].len > MaxStraight) return null;
            int n = (int)Math.Round(total / Spacing);
            float sp = (float)(total / n);
            var c = new Circuit();
            c.seed = seed; c.n = n; c.spacing = sp; c.length = (float)total;
            c.px = new float[n]; c.pz = new float[n]; c.py = new float[n]; c.tx = new float[n]; c.tz = new float[n]; c.nx = new float[n]; c.nz = new float[n];
            c.curv = new float[n]; c.halfW = new float[n]; c.bank = new float[n]; c.vcurv = new float[n]; c.s = new float[n];
            for (int side = 0; side < 2; side++) { c.kerb[side] = new float[n]; c.apron[side] = new float[n]; c.trap[side] = new float[n]; c.run[side] = new float[n]; }

            // walk the plan with exact arc formulas
            double x = 0, z = 0, th = 0, segStart = 0;
            int si = 0;
            for (int i = 0; i < n; i++)
            {
                double s = i * (double)sp;
                while (si < plan.Count - 1 && s >= segStart + plan[si].len)
                {
                    Seg e = plan[si];
                    if (e.curv == 0f) { x += Math.Sin(th) * e.len; z += Math.Cos(th) * e.len; }
                    else
                    {
                        double th2 = th + e.curv * e.len;
                        x += (Math.Cos(th) - Math.Cos(th2)) / e.curv; z += (Math.Sin(th2) - Math.Sin(th)) / e.curv; th = th2;
                    }
                    segStart += e.len; si++;
                }
                Seg g = plan[si];
                double t = s - segStart, ang = th, ox, oz;
                if (g.curv == 0f) { ox = x + Math.Sin(th) * t; oz = z + Math.Cos(th) * t; }
                else
                {
                    ang = th + g.curv * t;
                    ox = x + (Math.Cos(th) - Math.Cos(ang)) / g.curv; oz = z + (Math.Sin(ang) - Math.Sin(th)) / g.curv;
                }
                c.px[i] = (float)ox; c.pz[i] = (float)oz;
                c.tx[i] = (float)Math.Sin(ang); c.tz[i] = (float)Math.Cos(ang);
                c.nx[i] = c.tz[i]; c.nz[i] = -c.tx[i];
                c.curv[i] = g.curv;
                c.s[i] = i * sp;
                c.halfW[i] = RoadHalfWidth;
            }
            // the sum of the turns tells the direction of the lap
            double turn = 0;
            for (int i = 0; i < plan.Count; i++) turn += plan[i].curv * plan[i].len;
            c.clockwise = turn > 0;

            if (!relaxed && !ClearanceOk(c)) return null;

            // the main straight is a little wider
            for (int i = 0; i < n; i++)
            {
                float d = c.s[i] < c.length * 0.5f ? c.s[i] : c.length - c.s[i];
                c.halfW[i] = RoadHalfWidth + 1.0f * (1f - RacingMath.SmoothStep(180f, 380f, d));
            }

            Elevation(c, ref r);
            Dress(c, ref r);
            c.name = namer != null ? namer(r) : "Circuit " + seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            c.Finish();
            return c;
        }

        /// <summary>No two parts of the circuit, far apart along the lap, may pass closer than the two roads plus a margin.</summary>
        static bool ClearanceOk(Circuit c)
        {
            const int step = 4;
            for (int i = 0; i < c.n; i += step)
                for (int j = i + step; j < c.n; j += step)
                {
                    float dl = (j - i) * c.spacing;
                    if (dl > c.length * 0.5f) dl = c.length - dl;
                    if (dl < 90f) continue;
                    float dx = c.px[i] - c.px[j], dz = c.pz[i] - c.pz[j];
                    float need = 2f * RoadHalfWidth + 14f;
                    if (dx * dx + dz * dz < need * need) return false;
                }
            return true;
        }

        // ------------------------------------------------------------------ hills, crests and dips
        static void Elevation(Circuit c, ref Rng r)
        {
            int n = c.n;
            float amp = r.Range(5f, 12f);
            var am = new float[6]; var ph = new float[6];
            for (int k = 1; k <= 5; k++) { am[k] = amp / RacingMath.Pow(k, 1.15f) * r.Range(0.6f, 1f); ph[k] = r.Range(0f, RacingMath.TwoPi); }
            // a few localised crests (positive) and dips (negative), away from the grid
            int feats = 3 + r.Int(3);
            var fs = new float[feats]; var fh = new float[feats]; var fw = new float[feats];
            for (int k = 0; k < feats; k++)
            {
                fs[k] = r.Range(500f, c.length - 400f);
                fh[k] = (r.Chance(0.65f) ? 1f : -1f) * r.Range(0.8f, 1.6f);
                fw[k] = r.Range(45f, 85f);
            }
            var y = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = i * c.spacing;
                float v = 0f;
                for (int k = 1; k <= 5; k++) v += am[k] * RacingMath.Sin(RacingMath.TwoPi * k * s / c.length + ph[k]);
                for (int k = 0; k < feats; k++)
                {
                    float q = (s - fs[k]) / fw[k];
                    v += fh[k] * RacingMath.Exp(-q * q);
                }
                float d = s < c.length * 0.5f ? s : c.length - s;
                y[i] = v * RacingMath.SmoothStep(0f, 240f, d);
            }
            // never steeper than 8 %
            float maxSlope = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = i + 1 == n ? 0 : i + 1;
                float sl = RacingMath.Abs(y[j] - y[i]) / c.spacing;
                if (sl > maxSlope) maxSlope = sl;
            }
            float scale = maxSlope > 0.08f ? 0.08f / maxSlope : 1f;
            for (int i = 0; i < n; i++) c.py[i] = y[i] * scale;
            // vertical curvature (1/m), smoothed over ~8 m
            var vc = new float[n];
            for (int i = 0; i < n; i++)
            {
                int a = i == 0 ? n - 1 : i - 1, b = i + 1 == n ? 0 : i + 1;
                vc[i] = (c.py[a] - 2f * c.py[i] + c.py[b]) / (c.spacing * c.spacing);
            }
            for (int pass = 0; pass < 3; pass++)
            {
                var t = (float[])vc.Clone();
                for (int i = 0; i < n; i++)
                {
                    int a = i == 0 ? n - 1 : i - 1, b = i + 1 == n ? 0 : i + 1;
                    vc[i] = 0.25f * t[a] + 0.5f * t[i] + 0.25f * t[b];
                }
            }
            for (int i = 0; i < n; i++) c.vcurv[i] = vc[i];
        }

        // ------------------------------------------------------------------ kerbs, run-off, gravel, barrier, camber
        static void Dress(Circuit c, ref Rng r)
        {
            int n = c.n;
            // smoothed signed curvature over about +-45 m
            var ks = new float[n];
            int win = (int)(45f / c.spacing);
            for (int i = 0; i < n; i++)
            {
                float acc = 0f;
                for (int k = -win; k <= win; k++) acc += c.curv[(i + k + n * 4) % n];
                ks[i] = acc / (2 * win + 1);
            }
            // which corners get a gravel trap on the outside
            var trapOn = new bool[n];
            {
                int i = 0;
                // find the first straight sample to start from (the circuit starts on the main straight)
                while (i < n)
                {
                    if (RacingMath.Abs(ks[i]) > 1f / 250f)
                    {
                        int j = i;
                        while (j < n && RacingMath.Abs(ks[j]) > 1f / 250f) j++;
                        bool on = r.Chance(0.65f);
                        for (int k = i; k < j; k++) trapOn[k] = on;
                        i = j;
                    }
                    else i++;
                }
            }
            float grassBase = r.Range(4f, 7f);
            for (int i = 0; i < n; i++)
            {
                float k = ks[i];
                float corner = RacingMath.SmoothStep(1f / 520f, 1f / 220f, RacingMath.Abs(k));
                float cs = RacingMath.Abs(c.curv[i]);
                c.bank[i] = RacingMath.Clamp(1.2f * ks[i], -0.05f, 0.05f);
                for (int side = 0; side < 2; side++)
                {
                    // side 0 = left, 1 = right; the outside of a right turn (k > 0) is the left side
                    bool outside = side == 0 ? k > 0f : k < 0f;
                    float kerb = 1.2f * RacingMath.SmoothStep(1f / 450f, 1f / 300f, RacingMath.Abs(k));
                    float apron = outside ? RacingMath.Lerp(3.2f, 6.5f, corner) : RacingMath.Lerp(3.0f, 2.6f, corner);
                    float trap = outside && trapOn[i] ? RacingMath.Lerp(8f, 12f, 1f - RacingMath.SmoothStep(1f / 120f, 1f / 30f, RacingMath.Abs(k))) * corner : 0f;
                    float grass = grassBase + 2.5f * RacingMath.Sin(i * c.spacing / 73f + side * 1.7f);
                    float run = kerb + apron + trap + grass;
                    if (!outside && cs > 1e-5f)
                    {
                        float room = 1f / cs - c.halfW[i] - 2f;
                        if (run > room) run = room;
                    }
                    c.kerb[side][i] = kerb; c.apron[side][i] = apron; c.trap[side][i] = trap; c.run[side][i] = run;
                }
            }
            // squeeze the run-off where another part of the circuit is close, abeam of this one
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j += 2)
                {
                    float dl = RacingMath.Abs(j - i) * c.spacing;
                    if (dl > c.length * 0.5f) dl = c.length - dl;
                    if (dl < 90f) continue;
                    float dx = c.px[j] - c.px[i], dz = c.pz[j] - c.pz[i];
                    if (dx * dx + dz * dz > 100f * 100f) continue;
                    float along = dx * c.tx[i] + dz * c.tz[i];
                    if (RacingMath.Abs(along) > 25f) continue;
                    float lat = dx * c.nx[i] + dz * c.nz[i];
                    int side = lat > 0f ? 1 : 0;
                    float room = (RacingMath.Abs(lat) - c.halfW[j] - c.halfW[i]) * 0.5f - 0.3f;
                    if (room < 1.2f) room = 1.2f;
                    if (c.run[side][i] > room)
                    {
                        c.run[side][i] = room;
                        // the paved part and the gravel give way before the grass does
                        float used = c.kerb[side][i] + c.apron[side][i] + c.trap[side][i];
                        if (used > room)
                        {
                            float q = room / used;
                            c.kerb[side][i] *= q; c.apron[side][i] *= q; c.trap[side][i] *= q;
                        }
                    }
                }
            }
            // the barrier may not jump: it may come in at no more than 0.5 m per sample (1 m in 4 m of road) and never moves out
            for (int side = 0; side < 2; side++)
            {
                float[] rn = c.run[side];
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < n; i++) { int a = i == 0 ? n - 1 : i - 1; if (rn[i] > rn[a] + 0.5f) rn[i] = rn[a] + 0.5f; }
                for (int pass = 0; pass < 2; pass++)
                    for (int i = n - 1; i >= 0; i--) { int b = i + 1 == n ? 0 : i + 1; if (rn[i] > rn[b] + 0.5f) rn[i] = rn[b] + 0.5f; }
                for (int i = 0; i < n; i++)
                {
                    float used = c.kerb[side][i] + c.apron[side][i] + c.trap[side][i];
                    float room = rn[i] - 0.8f;
                    if (used > room)
                    {
                        float q = RacingMath.Max(room, 0.1f) / used;
                        c.kerb[side][i] *= q; c.apron[side][i] *= q; c.trap[side][i] *= q;
                    }
                }
            }
        }
    }
}
