using System;
using System.Collections.Generic;

namespace PryingEyes.Pista
{
    /// <summary>What a bike can do at full throttle and at full brake, speed by speed: measured by riding the physics model itself on flat clean asphalt (aids on), so the rider AI plans with
    /// numbers the bike really delivers. Cached per bike.</summary>
    public sealed class BikeCurves
    {
        public const float Step = 5f;
        public float[] accel, brake;
        public float topSpeed;

        static readonly Dictionary<string, BikeCurves> cache = new Dictionary<string, BikeCurves>();

        /// <summary>The curves of a bike on clean asphalt that has this share of the dry grip (1 = dry): the same bike brakes and pulls away worse on a wet road, and the plan must know it.</summary>
        public static BikeCurves For(BikeSpec spec, float grip = 1f)
        {
            BikeCurves c;
            string key = grip >= 0.999f ? spec.id : spec.id + "|" + grip.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            if (cache.TryGetValue(key, out c)) return c;
            c = Measure(spec, grip);
            cache[key] = c;
            return c;
        }

        public float AccelAt(float v) { return Sample(accel, v); }
        public float BrakeAt(float v) { return Sample(brake, v); }

        static float Sample(float[] t, float v)
        {
            float f = v / Step;
            int i = (int)f;
            if (i < 0) return t[0];
            if (i >= t.Length - 1) return t[t.Length - 1];
            return t[i] + (t[i + 1] - t[i]) * (f - i);
        }

        static BikeCurves Measure(BikeSpec spec, float grip)
        {
            var c = new BikeCurves();
            const int bins = 22;
            var accSum = new float[bins]; var accCnt = new int[bins]; var brkSum = new float[bins]; var brkCnt = new int[bins];
            var b = new BikeModel(spec, 1);
            var g = new FlatGround { grip = grip };
            float dt = 0.01f, t = 0f, prev = 0f, peak = 0f, still = 0f;
            var inp = new BikeInputs { throttle = 1f };
            while (t < 70f)
            {
                b.Step(dt, g, inp);
                t += dt;
                float a = (b.u - prev) / dt; prev = b.u;
                int bi = (int)(b.u / Step + 0.5f);
                if (bi < bins) { accSum[bi] += a; accCnt[bi]++; }
                if (b.u > peak) peak = b.u;
                if (a < 0.02f && b.u > 30f) still += dt; else still = 0f;
                if (still > 1.5f) break;
            }
            c.topSpeed = peak;
            // now the brakes, from the top speed down
            inp = new BikeInputs { brakeFront = 1f, brakeRear = 0.3f };
            prev = b.u; t = 0f;
            while (b.u > 0.8f && t < 40f)
            {
                b.Step(dt, g, inp);
                t += dt;
                float a = (prev - b.u) / dt; prev = b.u;
                int bi = (int)(b.u / Step + 0.5f);
                if (bi < bins) { brkSum[bi] += a; brkCnt[bi]++; }
            }
            int top = Math.Min(bins - 1, (int)(peak / Step) + 1);
            c.accel = new float[bins]; c.brake = new float[bins];
            float lastA = 6f, lastB = 9f;
            for (int i = 0; i < bins; i++)
            {
                float v = i * Step;
                c.accel[i] = accCnt[i] > 3 ? accSum[i] / accCnt[i] : (v > peak ? 0f : lastA);
                if (c.accel[i] < 0.05f && v <= peak) c.accel[i] = 0.05f;
                lastA = c.accel[i];
                c.brake[i] = brkCnt[i] > 3 ? brkSum[i] / brkCnt[i] : lastB;
                lastB = c.brake[i];
            }
            // a standing start is clutch-limited and the first bin averages the launch: never let it fall below the next one
            if (c.accel[0] < c.accel[1]) c.accel[0] = c.accel[1];
            if (c.brake[0] < c.brake[1]) c.brake[0] = c.brake[1];
            for (int i = 0; i < bins; i++) c.brake[i] = Math.Max(c.brake[i], 4f);
            return c;
        }
    }

    /// <summary>The line the riders follow: the path of least curvature across the width of the road (found by a bound-constrained least-squares solve on a coarse grid, so the corners are
    /// entered wide, clipped late and left wide), and the speed they can carry along it (the grip limit in every corner, then a forward pass for what the engine can accelerate and a backward pass
    /// for what the brakes can stop, both on the friction circle).</summary>
    public sealed class RacingLine
    {
        public readonly Circuit c;
        public readonly BikeSpec spec;
        public readonly BikeCurves curves;
        /// <summary>Per sample: lateral offset from the centreline (m, positive right), line position, heading and signed curvature (1/m).</summary>
        public float[] off, x, z, heading, kappa;
        /// <summary>Per sample: the grip-limited speed (m/s) and the planned speed after the acceleration and braking passes.</summary>
        public float[] vlim, vplan;
        /// <summary>Grip the plan uses (g's of side force) and the predicted lap time at the plan's pace.</summary>
        public float mu, lapTime;

        public RacingLine(Circuit circuit, BikeSpec bike, float muGrip = 1.05f, float margin = 1.4f, float smoothness = 0.08f, bool centreOnly = false)
        {
            c = circuit; spec = bike; mu = muGrip * circuit.GripScale;           // on a wet track the riders plan for the grip there is
            curves = BikeCurves.For(bike, circuit.GripScale);
            int n = c.n;
            off = new float[n]; x = new float[n]; z = new float[n]; heading = new float[n]; kappa = new float[n]; vlim = new float[n]; vplan = new float[n];
            if (!centreOnly) SolveOffsets(margin, smoothness);
            Geometry();
            Profile();
        }

        // ------------------------------------------------------------------ the line
        void SolveOffsets(float margin, float lambda)
        {
            int n = c.n;
            int m = Math.Max(24, (int)Math.Round(n / 5.0));
            var idx = new int[m];
            for (int k = 0; k < m; k++) idx[k] = (int)Math.Round((double)k * n / m) % n;
            var cx = new double[m]; var cz = new double[m]; var nx = new double[m]; var nz = new double[m]; var lo = new double[m]; var hi = new double[m];
            for (int k = 0; k < m; k++)
            {
                int i = idx[k];
                cx[k] = c.px[i]; cz[k] = c.pz[i]; nx[k] = c.nx[i]; nz[k] = c.nz[i];
                double lim = Math.Max(0.5, c.halfW[i] - margin);
                lo[k] = -lim; hi[k] = lim;
            }
            // dense matrix G and right-hand side b of the normal equations
            var G = new double[m * m]; var b = new double[m];
            for (int k = 0; k < m; k++)
            {
                int k1 = (k + 1) % m, k0 = (k + m - 1) % m;
                double ax = cx[k1] - 2 * cx[k] + cx[k0], az = cz[k1] - 2 * cz[k] + cz[k0];
                // the curvature term: pattern 1 -4 6 -4 1 weighted by the dot products of the normals
                for (int d = -2; d <= 2; d++)
                {
                    int j = (k + d + m) % m;
                    double w = d == 0 ? 6 : (d == 1 || d == -1) ? -4 : 1;
                    G[k * m + j] += w * (nx[k] * nx[j] + nz[k] * nz[j]);
                }
                // second difference of the centreline projected on the normal; the full second difference of a_m is needed: d_{k+1} - 2 d_k + d_{k-1}
                int k2 = (k + 2) % m, km2 = (k + m - 2) % m;
                double a1x = cx[k2] - 2 * cx[k1] + cx[k], a1z = cz[k2] - 2 * cz[k1] + cz[k];
                double a_1x = cx[k] - 2 * cx[k0] + cx[km2], a_1z = cz[k] - 2 * cz[k0] + cz[km2];
                double sx = a1x - 2 * ax + a_1x, sz = a1z - 2 * az + a_1z;
                b[k] += -(sx * nx[k] + sz * nz[k]);
                // the length term (pulls toward the shortest path)
                G[k * m + k] += lambda * 2;
                G[k * m + k1] += -lambda * (nx[k] * nx[k1] + nz[k] * nz[k1]);
                G[k * m + k0] += -lambda * (nx[k] * nx[k0] + nz[k] * nz[k0]);
                b[k] += lambda * (ax * nx[k] + az * nz[k]);
            }
            for (int k = 0; k < m; k++) G[k * m + k] += 1e-6;

            var o = new double[m];
            var fixedAt = new int[m];   // 0 free, +1 at the upper bound, -1 at the lower bound
            for (int outer = 0; outer < 14; outer++)
            {
                Solve(G, b, o, fixedAt, m, lo, hi);
                bool changed = false;
                for (int k = 0; k < m; k++)
                {
                    if (fixedAt[k] == 0)
                    {
                        if (o[k] > hi[k]) { o[k] = hi[k]; fixedAt[k] = 1; changed = true; }
                        else if (o[k] < lo[k]) { o[k] = lo[k]; fixedAt[k] = -1; changed = true; }
                    }
                }
                if (changed) continue;
                // release the bounds that want to move inward
                for (int k = 0; k < m; k++)
                {
                    if (fixedAt[k] == 0) continue;
                    double gk = -b[k];
                    for (int j = 0; j < m; j++) gk += G[k * m + j] * o[j];
                    if ((fixedAt[k] == 1 && gk > 1e-5) || (fixedAt[k] == -1 && gk < -1e-5)) { fixedAt[k] = 0; changed = true; }
                }
                if (!changed) break;
            }
            // back to every sample: a smooth (Catmull-Rom) interpolation between the nodes
            for (int i = 0; i < n; i++)
            {
                // the node interval that contains i
                int k = 0;
                // idx is ascending with idx[0] = 0
                int lo2 = 0, hi2 = m - 1;
                while (lo2 < hi2) { int mid = (lo2 + hi2 + 1) / 2; if (idx[mid] <= i) lo2 = mid; else hi2 = mid - 1; }
                k = lo2;
                int next = (k + 1) % m;
                int i0 = idx[k], i1 = idx[next] == 0 ? n : idx[next];
                double t = i1 > i0 ? (i - i0) / (double)(i1 - i0) : 0;
                double p0 = o[(k + m - 1) % m], p1 = o[k], p2 = o[next], p3 = o[(k + 2) % m];
                double v = 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
                float lim = Math.Max(0.5f, c.halfW[i] - margin);
                off[i] = PM.Clamp((float)v, -lim, lim);
            }
        }

        /// <summary>Cholesky solve of the free unknowns; the fixed ones are known and move to the right-hand side.</summary>
        static void Solve(double[] G, double[] b, double[] o, int[] fixedAt, int m, double[] lo, double[] hi)
        {
            var free = new List<int>();
            for (int k = 0; k < m; k++)
            {
                if (fixedAt[k] == 1) o[k] = hi[k]; else if (fixedAt[k] == -1) o[k] = lo[k]; else free.Add(k);
            }
            int f = free.Count;
            if (f == 0) return;
            var A = new double[f * f]; var r = new double[f];
            for (int a = 0; a < f; a++)
            {
                int k = free[a];
                double rr = b[k];
                for (int j = 0; j < m; j++) if (fixedAt[j] != 0) rr -= G[k * m + j] * o[j];
                r[a] = rr;
                for (int bb = 0; bb < f; bb++) A[a * f + bb] = G[k * m + free[bb]];
            }
            // Cholesky A = L L^T
            for (int i = 0; i < f; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    double sum = A[i * f + j];
                    for (int k = 0; k < j; k++) sum -= A[i * f + k] * A[j * f + k];
                    if (i == j) A[i * f + i] = Math.Sqrt(Math.Max(sum, 1e-12));
                    else A[i * f + j] = sum / A[j * f + j];
                }
            }
            for (int i = 0; i < f; i++)
            {
                double sum = r[i];
                for (int k = 0; k < i; k++) sum -= A[i * f + k] * r[k];
                r[i] = sum / A[i * f + i];
            }
            for (int i = f - 1; i >= 0; i--)
            {
                double sum = r[i];
                for (int k = i + 1; k < f; k++) sum -= A[k * f + i] * r[k];
                r[i] = sum / A[i * f + i];
            }
            for (int a = 0; a < f; a++) o[free[a]] = r[a];
        }

        void Geometry()
        {
            int n = c.n;
            for (int i = 0; i < n; i++) { x[i] = c.px[i] + c.nx[i] * off[i]; z[i] = c.pz[i] + c.nz[i] * off[i]; }
            for (int i = 0; i < n; i++)
            {
                int a = (i + n - 1) % n, b = (i + 1) % n;
                heading[i] = (float)Math.Atan2(x[b] - x[a], z[b] - z[a]);
            }
            const int w = 3;
            for (int i = 0; i < n; i++)
            {
                int a = (i + n - w) % n, b = (i + w) % n;
                float dh = PM.WrapPi(heading[b] - heading[a]);
                float dx = x[b] - x[a], dz = z[b] - z[a];
                float len = (float)Math.Sqrt(dx * dx + dz * dz) + 0.01f;
                kappa[i] = dh / len;
            }
        }

        // ------------------------------------------------------------------ the speed
        void Profile()
        {
            int n = c.n;
            float g = PM.G, top = curves.topSpeed * 0.985f;
            for (int i = 0; i < n; i++)
            {
                float k = PM.Abs(kappa[i]), vc = c.vcurv[i];
                float den = k - mu * vc;
                float v = den > 1e-6f ? PM.Sqrt(mu * g / den) : top;
                vlim[i] = PM.Min(v, top);
            }
            for (int i = 0; i < n; i++) vplan[i] = vlim[i];
            // the braking and the acceleration, twice around so the lap closes
            for (int pass = 0; pass < 2; pass++)
            {
                for (int s = 2 * n - 1; s >= 0; s--)
                {
                    int i = s % n, nx = (i + 1) % n;
                    float ds = Dist(i, nx);
                    float vn = vplan[nx];
                    float k = PM.Abs(kappa[i]);
                    float slope = (c.py[nx] - c.py[i]) / ds;
                    float vi = vplan[i];
                    float aLat = k * vi * vi;
                    float circle = PM.Sqrt(PM.Max(0f, 1f - PM.Sqr(PM.Min(1f, aLat / (mu * g)))));
                    float dec = curves.BrakeAt(vn) * circle + g * slope * 0.9f;
                    dec = PM.Max(dec, 1f);
                    float vmax = PM.Sqrt(vn * vn + 2f * dec * ds);
                    if (vmax < vplan[i]) vplan[i] = vmax;
                }
                for (int s = 0; s < 2 * n; s++)
                {
                    int i = s % n, nx = (i + 1) % n;
                    float ds = Dist(i, nx);
                    float vi = vplan[i];
                    float k = PM.Abs(kappa[nx]);
                    float slope = (c.py[nx] - c.py[i]) / ds;
                    float aLat = k * vi * vi;
                    float circle = PM.Sqrt(PM.Max(0f, 1f - PM.Sqr(PM.Min(1f, aLat / (mu * g)))));
                    float acc = curves.AccelAt(vi) * circle - g * slope * 0.9f;
                    acc = PM.Max(acc, 0.3f);
                    float vmax = PM.Sqrt(vi * vi + 2f * acc * ds);
                    if (vmax < vplan[nx]) vplan[nx] = vmax;
                }
            }
            float t = 0f;
            for (int i = 0; i < n; i++)
            {
                int nx = (i + 1) % n;
                t += Dist(i, nx) / (0.5f * (vplan[i] + vplan[nx]) + 0.1f);
            }
            lapTime = t;
        }

        float Dist(int i, int j)
        {
            float dx = x[j] - x[i], dz = z[j] - z[i];
            return PM.Sqrt(dx * dx + dz * dz) + 1e-4f;
        }

        // ------------------------------------------------------------------ queries by distance along the lap
        float F(float[] a, float s, out int i, out float t)
        {
            float f = c.WrapS(s) / c.spacing;
            i = (int)f; t = f - i;
            if (i >= c.n) { i -= c.n; }
            int j = i + 1 == c.n ? 0 : i + 1;
            return a[i] + (a[j] - a[i]) * t;
        }

        public float OffsetAt(float s) { int i; float t; return F(off, s, out i, out t); }
        public float PlanSpeedAt(float s) { int i; float t; return F(vplan, s, out i, out t); }
        public float CurvatureAt(float s) { int i; float t; return F(kappa, s, out i, out t); }

        /// <summary>The slowest planned speed between s and s + ahead (m): what the rider must be ready for.</summary>
        public float MinPlanAhead(float s, float ahead)
        {
            float m = float.MaxValue;
            for (float d = 0f; d <= ahead; d += c.spacing * 2f) m = PM.Min(m, PlanSpeedAt(s + d));
            return m;
        }

        /// <summary>The line's point at a distance along the lap (the centreline point of that distance pushed sideways by the offset).</summary>
        public void PointAt(float s, float extraOffset, out float px, out float pz)
        {
            float f = c.WrapS(s) / c.spacing;
            int i = (int)f; float t = f - i;
            i = c.Wrap(i);
            float cx = c.Lerp(c.px, i, t), cz = c.Lerp(c.pz, i, t);
            float nx = c.Lerp(c.nx, i, t), nz = c.Lerp(c.nz, i, t);
            float l = PM.Sqrt(nx * nx + nz * nz);
            nx /= l; nz /= l;
            float o = c.Lerp(off, i, t) + extraOffset;
            float lim = c.Lerp(c.halfW, i, t) - 0.9f;
            o = PM.Clamp(o, -lim, lim);
            px = cx + nx * o; pz = cz + nz * o;
        }
    }
}
