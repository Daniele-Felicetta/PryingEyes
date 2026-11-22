using System.Collections.Generic;
using PryingEyes.Deserto;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Pista
{
    /// <summary>The colours of one rider: the paint of the bike, its accent, the leathers (two tones) and the helmet.</summary>
    public sealed class Livery
    {
        public string name;
        /// <summary>The race number of the livery: the bike that wears it carries it on its plates.</summary>
        public int number;
        public Color main, accent, suit, suit2, helmet;

        static Color C(int hex) { return new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f); }

        static readonly Livery[] all =
        {
            new Livery { name = "Rosso Corsa", main = C(0xB3121C), accent = C(0xF2F2F0), suit = C(0xB3121C), suit2 = C(0x151515), helmet = C(0xF2F2F0) },
            new Livery { name = "Blu Notte", main = C(0x1B3F9E), accent = C(0xF3C21B), suit = C(0x1B3F9E), suit2 = C(0xF3C21B), helmet = C(0xE8E8F0) },
            new Livery { name = "Verde Bosco", main = C(0x1E7A3C), accent = C(0x101010), suit = C(0x101010), suit2 = C(0x1E7A3C), helmet = C(0x1E7A3C) },
            new Livery { name = "Arancio", main = C(0xE8650F), accent = C(0x15151A), suit = C(0x15151A), suit2 = C(0xE8650F), helmet = C(0xE8650F) },
            new Livery { name = "Bianco Perla", main = C(0xEDEDE8), accent = C(0xC4161C), suit = C(0xEDEDE8), suit2 = C(0xC4161C), helmet = C(0xC4161C) },
            new Livery { name = "Giallo Sole", main = C(0xF1C40F), accent = C(0x1A1A1A), suit = C(0xF1C40F), suit2 = C(0x1A1A1A), helmet = C(0x1A1A1A) },
            new Livery { name = "Nero Opaco", main = C(0x1B1B1F), accent = C(0xD7263D), suit = C(0x1B1B1F), suit2 = C(0xD7263D), helmet = C(0x1B1B1F) },
            new Livery { name = "Celeste", main = C(0x2FA7D8), accent = C(0xF2F2F0), suit = C(0x2FA7D8), suit2 = C(0x16202A), helmet = C(0xF2F2F0) },
            new Livery { name = "Viola", main = C(0x6A2C91), accent = C(0xF2B01E), suit = C(0x6A2C91), suit2 = C(0xF2B01E), helmet = C(0x6A2C91) },
            new Livery { name = "Argento", main = C(0xA7ABB2), accent = C(0x1B3F9E), suit = C(0x1B3F9E), suit2 = C(0xA7ABB2), helmet = C(0xA7ABB2) },
            new Livery { name = "Bordeaux", main = C(0x6B1226), accent = C(0xE6D9B8), suit = C(0x6B1226), suit2 = C(0xE6D9B8), helmet = C(0xE6D9B8) },
            new Livery { name = "Lime", main = C(0x8BC924), accent = C(0x151515), suit = C(0x151515), suit2 = C(0x8BC924), helmet = C(0x8BC924) },
        };

        static readonly int[] numbers = { 46, 93, 7, 21, 99, 12, 69, 34, 27, 4, 88, 52 };

        static Livery()
        {
            for (int i = 0; i < all.Length; i++) all[i].number = numbers[i % numbers.Length];
        }

        public static int Count => all.Length;
        public static Livery For(int skin) { return all[((skin % all.Length) + all.Length) % all.Length]; }

        /// <summary>The paint of a bike with the leathers and the helmet of another livery (the rider's own outfit, chosen in the garage).</summary>
        public static Livery WithRider(Livery bike, int outfit)
        {
            var o = For(outfit);
            return new Livery { name = bike.name, number = bike.number, main = bike.main, accent = bike.accent, suit = o.suit, suit2 = o.suit2, helmet = o.helmet };
        }
    }

    /// <summary>Everything the picture and the sound of one bike need, whether the bike is simulated on this machine (copied from the model) or arrives from the network (a friend's bike).</summary>
    public struct BikeView
    {
        public float x, y, z, heading, lean, pitch, steer, hang, tuck, speed, rpm, spinR, spinF, forkC, shockC, throttle, slipF, slipR;
        /// <summary>The rider's weight along the bike (-1 back .. 1 forward); only for the bikes simulated here.</summary>
        public float body;
        public int gear;
        public bool crashed, braking, slidingF, slidingR, scraping, absOn, tcsOn, shifting, limiter;
        public CrashKind crashKind;
        public SurfaceKind surface;

        public static BikeView From(BikeModel b, float throttle, bool braking)
        {
            var v = new BikeView();
            v.x = b.x; v.y = b.y; v.z = b.z; v.heading = b.heading; v.lean = b.lean; v.pitch = b.freePitch + b.pitch; v.steer = b.steerAngle; v.hang = b.hang; v.tuck = b.tuckAmt; v.body = b.body;
            v.speed = b.u; v.rpm = b.rpm; v.spinR = b.omegaR; v.spinF = b.omegaF; v.forkC = b.forkCompress; v.shockC = b.shockCompress; v.throttle = throttle; v.slipF = b.slipFront; v.slipR = b.slipRear;
            v.gear = b.gear; v.crashed = b.crashed; v.braking = braking; v.slidingF = b.frontSliding; v.slidingR = b.rearSliding; v.scraping = b.scraping; v.absOn = b.absActive; v.tcsOn = b.tcsActive;
            v.shifting = b.shifting; v.limiter = b.limiter; v.crashKind = b.crashKind; v.surface = b.surfaceR;
            return v;
        }

        /// <summary>Between two states of the physics (or two packets): the angles are interpolated the short way round.</summary>
        public static BikeView Lerp(BikeView a, BikeView b, float t)
        {
            var v = b;
            v.x = a.x + (b.x - a.x) * t; v.y = a.y + (b.y - a.y) * t; v.z = a.z + (b.z - a.z) * t;
            v.heading = a.heading + PM.WrapPi(b.heading - a.heading) * t;
            v.lean = a.lean + (b.lean - a.lean) * t; v.pitch = a.pitch + (b.pitch - a.pitch) * t; v.steer = a.steer + (b.steer - a.steer) * t;
            v.hang = a.hang + (b.hang - a.hang) * t; v.tuck = a.tuck + (b.tuck - a.tuck) * t; v.body = a.body + (b.body - a.body) * t; v.speed = a.speed + (b.speed - a.speed) * t; v.rpm = a.rpm + (b.rpm - a.rpm) * t;
            v.spinR = a.spinR + (b.spinR - a.spinR) * t; v.spinF = a.spinF + (b.spinF - a.spinF) * t; v.forkC = a.forkC + (b.forkC - a.forkC) * t; v.shockC = a.shockC + (b.shockC - a.shockC) * t;
            v.throttle = a.throttle + (b.throttle - a.throttle) * t;
            return v;
        }
    }

    /// <summary>The pieces of a bike in the scene: the body that leans and pitches, the steering with the fork and the front wheel, the swing arm and the rear wheel, the rider (hips, torso, head, legs, arms with
    /// their elbows worked out every frame) and the two lights.</summary>
    public sealed class PistaBikeRig
    {
        public Transform root, body, steer, frontPivot, rearPivot, swing, hips, torso, head, eye;
        public readonly Transform[] legs = new Transform[2], upper = new Transform[2], fore = new Transform[2], gloves = new Transform[2];
        public readonly Vector3[] shoulder = new Vector3[2], grip = new Vector3[2];
        public Material tailMat, lensMat, paint, paint2, suit, suit2, helmet, plate;
        /// <summary>The picture of the number plates (shared with every bike that wears the same number and colour).</summary>
        public Texture2D plateTex;
        public Quaternion steerRest;
        public Vector3 frontBase;
        public Vector3 hipBase;
        public readonly List<Renderer> riderParts = new List<Renderer>(), screenParts = new List<Renderer>();
        public float spinF, spinR;
        bool cockpit;

        /// <summary>The first-person view hides the rider's body and the windscreen (the camera is inside the helmet); the arms and the gloves stay.</summary>
        public void SetCockpit(bool on)
        {
            if (cockpit == on) return;
            cockpit = on;
            foreach (var r in riderParts) if (r != null) r.enabled = !on;
            foreach (var r in screenParts) if (r != null) r.enabled = !on;
        }

        public void SetShadows(bool on)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        /// <summary>Place the bike on the ground and pose every moving part from a view state.</summary>
        public void Pose(in BikeView v, float dt)
        {
            root.SetPositionAndRotation(new Vector3(v.x, v.y, v.z), Quaternion.Euler(0f, v.heading * Mathf.Rad2Deg, 0f));
            float leanDeg = v.lean * Mathf.Rad2Deg, pitchDeg = v.pitch * Mathf.Rad2Deg;
            body.localRotation = Quaternion.Euler(-pitchDeg, 0f, -leanDeg);
            steer.localRotation = steerRest * Quaternion.Euler(0f, v.steer * Mathf.Rad2Deg, 0f);
            frontPivot.localPosition = frontBase + new Vector3(0f, Mathf.Clamp(v.forkC, -0.05f, 0.18f), 0f);
            spinF = Mathf.Repeat(spinF + v.spinF * Mathf.Rad2Deg * dt, 360f);
            spinR = Mathf.Repeat(spinR + v.spinR * Mathf.Rad2Deg * dt, 360f);
            frontPivot.localRotation = Quaternion.Euler(spinF, 0f, 0f);
            rearPivot.localRotation = Quaternion.Euler(spinR, 0f, 0f);
            swing.localRotation = Quaternion.Euler(Mathf.Clamp(v.shockC * 28f, -4f, 7f), 0f, 0f);

            // the rider: shifts to the inside of the corner, folds behind the screen, the inside knee goes out
            float hang = Mathf.Clamp(v.hang, -1f, 1f);
            float fwd = Mathf.Clamp(v.body, -1f, 1f);
            hips.localPosition = hipBase + new Vector3(hang * 0.13f, -v.tuck * 0.04f - Mathf.Max(0f, fwd) * 0.015f, fwd * 0.08f);
            hips.localRotation = Quaternion.Euler(0f, 0f, -hang * 10f);
            torso.localRotation = Quaternion.Euler(v.tuck * 24f + fwd * 7f, hang * 6f, 0f);
            head.localRotation = Quaternion.Euler(-v.tuck * 14f, hang * 14f, hang * 8f);
            legs[1].localRotation = Quaternion.Euler(0f, 0f, Mathf.Max(0f, hang) * 24f);
            legs[0].localRotation = Quaternion.Euler(0f, 0f, -Mathf.Max(0f, -hang) * 24f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 s = torso.TransformPoint(shoulder[i]);
                Vector3 h = steer.TransformPoint(grip[i]);
                Vector3 d = h - s;
                float scale = Mathf.Max(0.5f, root.lossyScale.x);           // a 125 is a smaller bike than a 1300: the arms are the rider's, so they stay as long as they are
                float len = Mathf.Clamp(d.magnitude, 0.05f, 0.595f * scale);
                Vector3 dir = d.normalized;
                float L = 0.30f * scale;
                float half = len * 0.5f;
                float bend = Mathf.Sqrt(Mathf.Max(0f, L * L - half * half));
                Vector3 side = Vector3.Cross(dir, body.up).normalized;
                Vector3 outward = (i == 0 ? -1f : 1f) * body.right;
                Vector3 bendDir = (outward * 0.55f - body.up * 0.45f + body.forward * 0.1f).normalized;
                bendDir = (bendDir - dir * Vector3.Dot(bendDir, dir)).normalized;
                if (bendDir.sqrMagnitude < 0.5f) bendDir = side;
                Vector3 elbow = s + dir * half + bendDir * bend;
                SetRod(upper[i], s, elbow);
                SetRod(fore[i], elbow, h);
                gloves[i].position = h;
                gloves[i].rotation = steer.rotation;
            }
        }

        static void SetRod(Transform t, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            t.SetPositionAndRotation(a, Quaternion.LookRotation(d / len, Vector3.up));
            float parentScale = t.parent != null ? Mathf.Max(0.01f, t.parent.lossyScale.z) : 1f;
            t.localScale = new Vector3(1f, 1f, len / parentScale);
        }

        /// <summary>The brake light and the headlamp glow.</summary>
        public void Lights(bool braking, bool dark)
        {
            if (tailMat != null) tailMat.SetColor("_EmissionColor", new Color(1f, 0.05f, 0.03f) * (braking ? 3.2f : dark ? 1.2f : 0.5f));
            if (lensMat != null) lensMat.SetColor("_EmissionColor", new Color(1f, 0.95f, 0.8f) * (dark ? 3.0f : 0.8f));
        }
    }

    /// <summary>The sport bikes drawn by code: lofted fairing, tank, seat and tail, an engine and an exhaust, upside-down fork, swing arm, spoked wheels with discs, and a rider in leathers with a
    /// helmet whose arms reach the grips. Three styles (superbike, supersport, naked roadster) share the parts; the paint is the livery of the rider. The meshes are built once and shared, the materials
    /// that carry colours are made per bike.</summary>
    public static class PistaBikeMesh
    {
        const float R = 0.31f, WB = 1.40f, Rake = 24f;
        static readonly Vector3 AxleF = new Vector3(0f, R, WB * 0.5f), AxleR = new Vector3(0f, R, -WB * 0.5f);
        static readonly Vector3 Head = AxleF + new Vector3(0f, Mathf.Cos(Rake * Mathf.Deg2Rad), -Mathf.Sin(Rake * Mathf.Deg2Rad)) * 0.64f;
        static readonly Vector3 Hip = new Vector3(0f, 0.90f, -0.28f);
        static readonly Matrix4x4 I = Matrix4x4.identity;

        sealed class Kit
        {
            public Mesh plates, frame, engine, exhaust, paint, paint2, seat, screen, fork, bars, swing, tyreF, tyreR, rimF, rimR, disc, mirrors, lens, lensBody, nose, tail, forkLow, fender;
            public Mesh torso, torsoTrim, kneeTrim, helmet, visor, leg, boot, glove, upperUnit, foreUnit;
            public Material mFrame, mMetal, mSteel, mTitan, mRubber, mSeat, mGlass, mGold, mGlove, mBoot, mVisor;
            public Matrix4x4 steerM;
            public Quaternion steerRot;
        }

        static readonly Dictionary<int, Kit> kits = new Dictionary<int, Kit>();

        struct Ring
        {
            public float z, hw, y0, y1, p;
            public Ring(float z, float hw, float y0, float y1, float p = 2.6f) { this.z = z; this.hw = hw; this.y0 = y0; this.y1 = y1; this.p = p; }
        }

        /// <summary>A smooth closed body along z through cross sections (a rounded box whose roundness is p: 2 an ellipse, 4 nearly square).</summary>
        static Mesh Loft(string name, Ring[] rings, int sides = 16)
        {
            var v = new List<Vector3>(rings.Length * sides);
            for (int i = 0; i < rings.Length; i++)
            {
                var r = rings[i];
                float ym = (r.y0 + r.y1) * 0.5f, hh = (r.y1 - r.y0) * 0.5f;
                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides, c = Mathf.Cos(a), s = Mathf.Sin(a);
                    float e = 2f / r.p;
                    v.Add(new Vector3(r.hw * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e), ym + hh * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), e), r.z));
                }
            }
            var t = new List<int>();
            for (int i = 0; i + 1 < rings.Length; i++)
                for (int j = 0; j < sides; j++)
                {
                    int j1 = (j + 1) % sides;
                    int a = i * sides + j, b = i * sides + j1, c = (i + 1) * sides + j1, d = (i + 1) * sides + j;
                    Vector3 outward = new Vector3(v[a].x, v[a].y - (rings[i].y0 + rings[i].y1) * 0.5f, 0f);
                    if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outward) >= 0f) { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }
                    else { t.Add(a); t.Add(c); t.Add(b); t.Add(a); t.Add(d); t.Add(c); }
                }
            // close both ends with a fan: the first and the last section are small but not nothing, and the tail of the helmet is looked at from behind all the time (it was a hole through which
            // the back of the other side of the shell could be seen)
            for (int e = 0; e < 2 && rings.Length >= 2; e++)
            {
                int ri = e == 0 ? 0 : rings.Length - 1, rj = e == 0 ? 1 : rings.Length - 2;
                float dir = rings[ri].z >= rings[rj].z ? 1f : -1f;                                  // the way the cap faces along z: away from the body
                int c0 = v.Count;
                v.Add(new Vector3(0f, (rings[ri].y0 + rings[ri].y1) * 0.5f, rings[ri].z));
                for (int j = 0; j < sides; j++)
                {
                    int a = ri * sides + j, b = ri * sides + (j + 1) % sides;
                    if (Vector3.Cross(v[a] - v[c0], v[b] - v[c0]).z * dir >= 0f) { t.Add(c0); t.Add(a); t.Add(b); } else { t.Add(c0); t.Add(b); t.Add(a); }
                }
            }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Vector3[] P(params float[] c)
        {
            var o = new Vector3[c.Length / 3];
            for (int i = 0; i < o.Length; i++) o[i] = new Vector3(c[i * 3], c[i * 3 + 1], c[i * 3 + 2]);
            return o;
        }

        static Vector3[] Smooth(Vector3[] p, int per)
        {
            var o = new List<Vector3>();
            for (int i = 0; i < p.Length - 1; i++)
            {
                Vector3 p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, p.Length - 1)];
                for (int k = 0; k < per; k++)
                {
                    float t = k / (float)per, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(p[p.Length - 1]);
            return o.ToArray();
        }

        static float[] Fill(int n, float r) { var o = new float[n]; for (int i = 0; i < n; i++) o[i] = r; return o; }

        static (Mesh mesh, Matrix4x4 at) Rod(Vector3 a, Vector3 b, float r, int sides = 8) => (DesertLathe.Tube("asta", new[] { a, b }, new[] { r, r }, sides), I);
        static (Mesh mesh, Matrix4x4 at) Path(Vector3[] path, float r, int sides = 8) { var q = Smooth(path, 4); return (DesertLathe.Tube("tubo", q, Fill(q.Length, r), sides), I); }
        static (Mesh mesh, Matrix4x4 at) Turn(Vector2[] prof, int sides, Matrix4x4 at) => (DesertLathe.Turn("tornio", prof, sides), at);
        static Matrix4x4 At(Vector3 pos, Quaternion rot) => Matrix4x4.TRS(pos, rot, Vector3.one);
        static Matrix4x4 At(Vector3 pos, Quaternion rot, Vector3 scale) => Matrix4x4.TRS(pos, rot, scale);

        static Mesh Cube(Vector3 size)
        {
            var b = new MB(1);
            b.Box(0, Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, size * 0.5f);
            return b.ToMesh("cubo");
        }

        static Mesh Join(string name, List<(Mesh mesh, Matrix4x4 at)> parts) => DesertLathe.Join(name, parts);

        /// <summary>A quad carrying the picture of a number plate, added to the lists: it lies over the face of a part placed at 'at' with 'rot' (the face whose normal is 'face' in the part's own frame), 'lift'
        /// metres out of it so that it does not fight with the part, 'w' wide and 'h' high, the picture reading from left to right when seen from the front.</summary>
        static void PlateQuad(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 at, Quaternion rot, Vector3 face, float w, float h, float lift)
        {
            Vector3 n = rot * face, up = rot * Vector3.up;
            Vector3 right = Vector3.Cross(n, up).normalized;                    // what is on the right of a person looking at the plate
            Vector3 c = at + n * lift;
            int b = v.Count;
            v.Add(c - right * (w * 0.5f) - up * (h * 0.5f)); uv.Add(new Vector2(0f, 0f));
            v.Add(c - right * (w * 0.5f) + up * (h * 0.5f)); uv.Add(new Vector2(0f, 1f));
            v.Add(c + right * (w * 0.5f) + up * (h * 0.5f)); uv.Add(new Vector2(1f, 1f));
            v.Add(c + right * (w * 0.5f) - up * (h * 0.5f)); uv.Add(new Vector2(1f, 0f));
            t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
        }

        /// <summary>A round tire of a given section width, lying with its axis along x, centre at the origin.</summary>
        static Mesh Tyre(float width, float radius)
        {
            var prof = new List<Vector2>();
            float th = 0.085f, hw = width * 0.5f;
            prof.Add(new Vector2(radius - th - 0.004f, -hw * 0.82f));
            for (int k = 0; k <= 12; k++)
            {
                float a = (-1f + 2f * k / 12f) * 1.50f;
                prof.Add(new Vector2(radius - th + th * Mathf.Pow(Mathf.Cos(a), 0.65f), hw * Mathf.Sin(a) / Mathf.Sin(1.50f)));
            }
            prof.Add(new Vector2(radius - th - 0.004f, hw * 0.82f));
            // the lathe turns about y: lay the tyre on its side so that the axle runs along x like the rim's
            return Join("gomma", new List<(Mesh mesh, Matrix4x4 at)> { (DesertLathe.Turn("gomma", prof.ToArray(), 30), At(Vector3.zero, Quaternion.Euler(0f, 0f, 90f))) });
        }

        static Mesh Rim(float width, float radius, bool five)
        {
            var parts = new List<(Mesh mesh, Matrix4x4 at)>();
            float hw = width * 0.5f;
            var axis = Quaternion.Euler(0f, 0f, 90f);
            parts.Add((DesertLathe.Turn("cerchio", new[] { new Vector2(radius - 0.085f, -hw), new Vector2(radius - 0.075f, -hw), new Vector2(radius - 0.075f, hw), new Vector2(radius - 0.085f, hw) }, 28), At(Vector3.zero, axis)));
            parts.Add((DesertLathe.Turn("mozzo", new[] { new Vector2(0f, -0.055f), new Vector2(0.035f, -0.055f), new Vector2(0.035f, 0.055f), new Vector2(0f, 0.055f) }, 12), At(Vector3.zero, axis)));
            int blades = five ? 5 : 8;
            for (int i = 0; i < blades; i++)
            {
                var rot = Quaternion.Euler(i * 360f / blades, 0f, 0f);
                float len = radius - 0.085f - 0.03f;
                parts.Add((Cube(new Vector3(0.026f, len, 0.034f)), At(rot * new Vector3(0f, 0.03f + len * 0.5f, 0f), rot)));
            }
            return Join("raggi", parts);
        }

        static Kit Ensure(int style)
        {
            if (kits.TryGetValue(style, out var have) && have.frame != null && have.mFrame != null) return have;
            var k = new Kit();
            bool fairing = style < 2;
            float s = style == 1 ? 0.97f : 1f;
            k.steerRot = Quaternion.FromToRotation(Vector3.up, (Head - AxleF).normalized);
            k.steerM = Matrix4x4.TRS(Head, k.steerRot, Vector3.one);
            Matrix4x4 inv = k.steerM.inverse;

            // ---- frame, engine, exhaust (not painted)
            var frame = new List<(Mesh mesh, Matrix4x4 at)>();
            foreach (float sd in new[] { -1f, 1f })
            {
                frame.Add(Path(P(sd * 0.075f, 0.88f, 0.34f, sd * 0.10f, 0.74f, 0.05f, sd * 0.11f, 0.52f, -0.30f), 0.026f, 10));
                frame.Add(Path(P(sd * 0.10f, 0.58f, -0.26f, sd * 0.09f, 0.78f, -0.40f, sd * 0.07f, 0.86f, -0.82f), 0.017f, 8));
                frame.Add(Rod(new Vector3(sd * 0.10f, 0.48f, -0.30f), new Vector3(sd * 0.10f, 0.44f, 0.12f), 0.02f, 8));
                frame.Add((Cube(new Vector3(0.05f, 0.02f, 0.12f)), At(new Vector3(sd * 0.24f, 0.40f, -0.22f), Quaternion.identity)));       // pegs
                frame.Add((Cube(new Vector3(0.05f, 0.02f, 0.10f)), At(new Vector3(sd * 0.22f, 0.50f, 0.20f), Quaternion.identity)));
            }
            k.frame = Join("telaio", frame);
            // the engine: crankcase, the cylinders leaning forward with their cooling fins, the head and its cover, the clutch and alternator covers, and the radiator in front
            const float Tilt = 18f;
            var axis = new Vector3(0f, Mathf.Cos(Tilt * Mathf.Deg2Rad), Mathf.Sin(Tilt * Mathf.Deg2Rad));
            var tilt = Quaternion.Euler(Tilt, 0f, 0f);
            Vector3 foot = new Vector3(0f, 0.47f, 0.10f);
            var eng = new List<(Mesh mesh, Matrix4x4 at)>
            {
                (Loft("carter", new[] { new Ring(-0.20f, 0.07f, 0.28f, 0.42f, 3f), new Ring(-0.12f, 0.12f, 0.25f, 0.47f, 3f), new Ring(0.05f, 0.125f, 0.25f, 0.48f, 3f), new Ring(0.17f, 0.105f, 0.27f, 0.45f, 3f), new Ring(0.22f, 0.05f, 0.31f, 0.41f, 3f) }, 16), I),
                (Cube(new Vector3(0.20f, 0.17f, 0.18f)), At(foot + axis * 0.115f, tilt)),
                (Cube(new Vector3(0.21f, 0.06f, 0.20f)), At(foot + axis * 0.235f, tilt)),
                (Cube(new Vector3(0.18f, 0.035f, 0.17f)), At(foot + axis * 0.285f, tilt)),
                Turn(new[] { new Vector2(0f, -0.014f), new Vector2(0.085f, -0.014f), new Vector2(0.09f, 0f), new Vector2(0.085f, 0.014f), new Vector2(0f, 0.014f) }, 18, At(new Vector3(0.135f, 0.38f, 0.0f), Quaternion.Euler(0f, 0f, 90f))),
                Turn(new[] { new Vector2(0f, -0.014f), new Vector2(0.07f, -0.014f), new Vector2(0.075f, 0f), new Vector2(0.07f, 0.014f), new Vector2(0f, 0.014f) }, 18, At(new Vector3(-0.135f, 0.42f, -0.10f), Quaternion.Euler(0f, 0f, 90f))),
                (Cube(new Vector3(0.23f, 0.25f, 0.04f)), At(new Vector3(0f, 0.55f, 0.37f), Quaternion.Euler(-12f, 0f, 0f))),
            };
            for (int i = 0; i < 5; i++) eng.Add((Cube(new Vector3(0.245f, 0.009f, 0.235f)), At(foot + axis * (0.03f + i * 0.036f), tilt)));
            k.engine = Join("motore", eng);
            // the exhaust: two headers join into a collector under the engine and a silencer that rises toward the tail
            var ex = new List<(Mesh mesh, Matrix4x4 at)>();
            ex.Add(Path(P(0.045f, 0.66f, 0.30f, 0.075f, 0.50f, 0.38f, 0.11f, 0.32f, 0.24f, 0.12f, 0.27f, 0.02f, 0.12f, 0.285f, -0.16f), 0.019f, 10));
            ex.Add(Path(P(-0.045f, 0.66f, 0.30f, -0.07f, 0.49f, 0.37f, -0.03f, 0.30f, 0.22f, 0.09f, 0.26f, 0.0f, 0.12f, 0.285f, -0.16f), 0.019f, 10));
            ex.Add(Limb(P(0.12f, 0.285f, -0.16f, 0.15f, 0.33f, -0.38f, 0.17f, 0.41f, -0.60f, 0.185f, 0.50f, -0.82f), new[] { 0.040f, 0.050f, 0.054f, 0.050f }, 16));
            ex.Add(Turn(new[] { new Vector2(0f, -0.006f), new Vector2(0.044f, -0.006f), new Vector2(0.047f, 0f), new Vector2(0.044f, 0.006f), new Vector2(0f, 0.006f) }, 16,
                At(new Vector3(0.1845f, 0.502f, -0.828f), Quaternion.FromToRotation(Vector3.up, new Vector3(0.015f, 0.09f, -0.22f).normalized))));
            k.exhaust = Join("scarico", ex);

            // ---- bodywork (the livery colour): the fairing ends before the knees, then tank, seat cover and tail
            var paint = new List<(Mesh mesh, Matrix4x4 at)>();
            if (fairing)
            {
                paint.Add((Loft("carena", new[]
                {
                    new Ring(0.86f, 0.012f, 0.73f, 0.77f, 2.4f), new Ring(0.80f, 0.075f, 0.68f, 0.82f, 2.8f), new Ring(0.68f, 0.14f * s, 0.60f, 0.92f, 3.0f), new Ring(0.52f, 0.19f * s, 0.52f, 0.99f, 3.0f),
                    new Ring(0.36f, 0.225f * s, 0.47f, 1.00f, 3.0f), new Ring(0.22f, 0.235f * s, 0.46f, 0.96f, 3.0f), new Ring(0.10f, 0.225f * s, 0.46f, 0.88f, 3.0f), new Ring(0.02f, 0.19f * s, 0.48f, 0.80f, 2.8f),
                    new Ring(-0.03f, 0.12f * s, 0.52f, 0.74f, 2.4f),
                }, 24), I));
            }
            paint.Add((Loft("serbatoio", new[]
            {
                new Ring(0.44f, 0.045f, 0.86f, 0.96f), new Ring(0.33f, 0.12f * s, 0.80f, 1.00f), new Ring(0.12f, 0.185f * s, 0.77f, 1.02f), new Ring(-0.10f, 0.17f * s, 0.78f, 0.99f), new Ring(-0.22f, 0.12f, 0.80f, 0.93f),
            }, 20), I));
            paint.Add((Loft("codone", new[]
            {
                new Ring(-0.46f, 0.09f, 0.81f, 0.925f), new Ring(-0.62f, 0.115f, 0.82f, 0.955f), new Ring(-0.78f, 0.10f, 0.85f, 0.985f), new Ring(-0.90f, 0.062f, 0.89f, 0.995f), new Ring(-0.96f, 0.02f, 0.925f, 0.985f),
            }, 18), I));
            if (!fairing)
            {
                // roadster: the headlamp pod and a small flyscreen
                paint.Add(Turn(new[] { new Vector2(0f, -0.10f), new Vector2(0.085f, -0.10f), new Vector2(0.13f, -0.05f), new Vector2(0.145f, 0.02f), new Vector2(0.12f, 0.09f), new Vector2(0.05f, 0.12f), new Vector2(0f, 0.12f) }, 16,
                    At(new Vector3(0f, 0.97f, 0.58f), Quaternion.Euler(90f, 0f, 0f), new Vector3(1.05f, 1f, 1f))));
            }
            k.paint = Join("vernice", paint);
            var paint2 = new List<(Mesh mesh, Matrix4x4 at)>();
            if (fairing)
                paint.Add((Loft("pancia", new[] { new Ring(0.56f, 0.05f, 0.23f, 0.37f, 3f), new Ring(0.40f, 0.13f * s, 0.21f, 0.42f, 3f), new Ring(0.15f, 0.185f * s, 0.21f, 0.44f, 3f), new Ring(-0.10f, 0.175f * s, 0.23f, 0.42f, 3f), new Ring(-0.26f, 0.11f, 0.26f, 0.40f, 3f), new Ring(-0.34f, 0.04f, 0.29f, 0.37f, 3f) }, 20), I));
            // the number plates: a thin back in the accent colour, and over its face the picture of the plate (a quad with the number on it; one material for the bike, see PistaPlates)
            var plV = new List<Vector3>(); var plU = new List<Vector2>(); var plT = new List<int>();
            Vector3 frontAt = new Vector3(0f, fairing ? 0.80f : 0.99f, fairing ? 0.80f : 0.68f); Quaternion frontQ = Quaternion.Euler(-30f, 0f, 0f);
            paint2.Add((Cube(new Vector3(0.212f, 0.140f, 0.012f)), At(frontAt, frontQ)));
            PlateQuad(plV, plU, plT, frontAt, frontQ, Vector3.forward, 0.200f, 0.125f, 0.0066f);
            // the side plates of the tail: flush with the cowl (it is about 0.10 wide there) and raked like it
            Vector3 rightAt = new Vector3(0.1135f, 0.89f, -0.64f), leftAt = new Vector3(-0.1135f, 0.89f, -0.64f);
            Quaternion rightQ = Quaternion.Euler(0f, 0f, -5f), leftQ = Quaternion.Euler(0f, 0f, 5f);
            paint2.Add((Cube(new Vector3(0.008f, 0.10f, 0.16f)), At(rightAt, rightQ)));
            paint2.Add((Cube(new Vector3(0.008f, 0.10f, 0.16f)), At(leftAt, leftQ)));
            PlateQuad(plV, plU, plT, rightAt, rightQ, Vector3.right, 0.150f, 0.094f, 0.0046f);
            PlateQuad(plV, plU, plT, leftAt, leftQ, Vector3.left, 0.150f, 0.094f, 0.0046f);
            // and one under the tail light, looking back: it is what the camera behind the bike sees
            Vector3 rearAt = new Vector3(0f, 0.865f, -0.99f);
            paint2.Add((Cube(new Vector3(0.200f, 0.128f, 0.008f)), At(rearAt, Quaternion.identity)));
            PlateQuad(plV, plU, plT, rearAt, Quaternion.identity, Vector3.back, 0.190f, 0.119f, 0.0046f);
            k.paint2 = Join("vernice2", paint2);
            var plateMesh = new Mesh { name = "targhe" };
            plateMesh.SetVertices(plV); plateMesh.SetUVs(0, plU); plateMesh.SetTriangles(plT, 0);
            plateMesh.RecalculateNormals(); plateMesh.RecalculateBounds();
            k.plates = plateMesh;

            // ---- seat
            k.seat = Join("sella", new List<(Mesh mesh, Matrix4x4 at)> { (Loft("sella", new[] { new Ring(-0.05f, 0.06f, 0.865f, 0.88f), new Ring(-0.20f, 0.13f, 0.85f, 0.905f), new Ring(-0.45f, 0.12f, 0.85f, 0.915f), new Ring(-0.62f, 0.09f, 0.86f, 0.90f) }, 16), I) });

            // ---- windscreen (a tinted plate that follows the slope up from the fairing) and mirrors (screen parts are hidden in the cockpit view)
            var scr = new List<(Mesh mesh, Matrix4x4 at)>();
            if (fairing)
                scr.Add((Loft("cupolino", new[] { new Ring(0f, 0.17f * s, -0.005f, 0.005f, 2.4f), new Ring(0.12f, 0.165f * s, -0.005f, 0.005f, 2.4f), new Ring(0.24f, 0.14f * s, -0.005f, 0.005f, 2.4f), new Ring(0.33f, 0.085f * s, -0.005f, 0.005f, 2.2f) }, 18),
                    At(new Vector3(0f, 0.985f, 0.545f), Quaternion.LookRotation(new Vector3(0f, 0.55f, -0.83f), Vector3.up))));
            else scr.Add((Cube(new Vector3(0.22f, 0.12f, 0.010f)), At(new Vector3(0f, 1.10f, 0.52f), Quaternion.Euler(-36f, 0f, 0f))));
            k.screen = Join("cupolino", scr);
            var mir = new List<(Mesh mesh, Matrix4x4 at)>();
            foreach (float sd in new[] { -1f, 1f })
            {
                mir.Add(Rod(new Vector3(sd * 0.20f, fairing ? 1.00f : 0.97f, 0.40f), new Vector3(sd * 0.30f, 1.065f, 0.35f), 0.008f, 6));
                mir.Add(Ball(0.04f, new Vector3(sd * 0.32f, 1.075f, 0.335f), new Vector3(1.25f, 0.75f, 0.32f), Quaternion.Euler(0f, sd * -15f, 0f)));
            }
            k.mirrors = Join("specchi", mir);
            var nose = new List<(Mesh mesh, Matrix4x4 at)>();
            if (fairing)
            {
                nose.Add(Ball(0.05f, new Vector3(0f, 0.735f, 0.835f), new Vector3(1.5f, 0.5f, 0.55f), Quaternion.Euler(-20f, 0f, 0f)));
                foreach (float sd in new[] { -1f, 1f }) nose.Add((Cube(new Vector3(0.012f, 0.05f, 0.14f)), At(new Vector3(sd * 0.205f, 0.66f, 0.34f), Quaternion.Euler(0f, sd * 8f, 0f))));
            }
            k.nose = fairing ? Join("prese", nose) : null;

            // ---- steering: upside-down fork, triple clamp and clip-on bars, in the frame of the steering axis
            var fork = new List<(Mesh mesh, Matrix4x4 at)>();
            var forkLow = new List<(Mesh mesh, Matrix4x4 at)>();
            var bars = new List<(Mesh mesh, Matrix4x4 at)>();
            float axisLen = (Head - AxleF).magnitude;
            foreach (float sd in new[] { -1f, 1f })
            {
                fork.Add(Rod(new Vector3(sd * 0.09f, -0.04f, 0f), new Vector3(sd * 0.09f, -axisLen * 0.58f, 0f), 0.024f, 10));
                forkLow.Add(Rod(new Vector3(sd * 0.09f, -axisLen * 0.56f, 0f), new Vector3(sd * 0.09f, -axisLen + 0.02f, 0f), 0.019f, 8));
            }
            bars.Add((Cube(new Vector3(0.24f, 0.035f, 0.075f)), At(new Vector3(0f, -0.02f, 0f), Quaternion.identity)));
            bars.Add((Cube(new Vector3(0.22f, 0.03f, 0.07f)), At(new Vector3(0f, -0.15f, 0f), Quaternion.identity)));
            foreach (float sd in new[] { -1f, 1f })
            {
                Vector3 g = inv.MultiplyPoint3x4(new Vector3(sd * 0.30f, fairing ? 0.905f : 1.00f, fairing ? 0.40f : 0.34f));
                Vector3 st = new Vector3(sd * 0.095f, -0.04f, 0.02f);
                bars.Add(Path(new[] { st, Vector3.Lerp(st, g, 0.5f) + new Vector3(0f, 0.02f, 0.02f), g }, 0.012f, 6));
                bars.Add(Rod(g, g + new Vector3(sd * 0.11f, 0f, 0f), 0.018f, 8));
            }
            // the front mudguard turns with the fork: an arc over the front wheel, drawn in the frame of the bike and brought into the steering frame
            var arc = new Vector3[8];
            for (int i = 0; i < arc.Length; i++)
            {
                float a = Mathf.Lerp(22f, 128f, i / (float)(arc.Length - 1)) * Mathf.Deg2Rad;
                arc[i] = new Vector3(0f, Mathf.Sin(a) * (R + 0.045f), Mathf.Cos(a) * (R + 0.045f));
            }
            var fenderArc = Smooth(arc, 3);
            k.fender = Join("parafango", new List<(Mesh mesh, Matrix4x4 at)> { (DesertLathe.Tube("parafango", fenderArc, Fill(fenderArc.Length, 0.028f), 6), inv * At(AxleF, Quaternion.identity, new Vector3(2.3f, 1f, 1f))) });
            k.fork = Join("forcella", fork); k.forkLow = Join("steli", forkLow); k.bars = Join("manubrio", bars);
            k.lens = Join("fari", new List<(Mesh mesh, Matrix4x4 at)>
            {
                Ball(0.07f, inv.MultiplyPoint3x4(new Vector3(0f, 0.97f, 0.715f)), new Vector3(1f, 1f, 0.5f), Quaternion.Inverse(k.steerRot)),
            });
            k.lensBody = Join("fari", new List<(Mesh mesh, Matrix4x4 at)>
            {
                Ball(0.05f, new Vector3(0.085f, 0.775f, 0.775f), new Vector3(1.35f, 0.62f, 0.45f), Quaternion.Euler(-14f, 18f, 0f)),
                Ball(0.05f, new Vector3(-0.085f, 0.775f, 0.775f), new Vector3(1.35f, 0.62f, 0.45f), Quaternion.Euler(-14f, -18f, 0f)),
            });
            k.tail = Cube(new Vector3(0.14f, 0.03f, 0.015f));

            // ---- swing arm
            var sw = new List<(Mesh mesh, Matrix4x4 at)>();
            foreach (float sd in new[] { -1f, 1f })
                sw.Add(Path(P(sd * 0.10f, 0.45f, -0.30f, sd * 0.10f, 0.38f, -0.50f, sd * 0.095f, R, -0.70f), 0.024f, 8));
            sw.Add(Rod(new Vector3(-0.10f, 0.42f, -0.34f), new Vector3(0.10f, 0.42f, -0.34f), 0.02f, 6));
            k.swing = Join("forcellone", sw);

            // ---- wheels
            k.tyreF = Tyre(0.125f, R); k.tyreR = Tyre(0.19f, R);
            k.rimF = Rim(0.09f, R, true); k.rimR = Rim(0.13f, R, true);
            var disc = new List<(Mesh mesh, Matrix4x4 at)>
            {
                (DesertLathe.Turn("disco", new[] { new Vector2(0.10f, -0.004f), new Vector2(0.16f, -0.004f), new Vector2(0.16f, 0.004f), new Vector2(0.10f, 0.004f) }, 24), At(new Vector3(0.05f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f))),
                (DesertLathe.Turn("disco", new[] { new Vector2(0.10f, -0.004f), new Vector2(0.16f, -0.004f), new Vector2(0.16f, 0.004f), new Vector2(0.10f, 0.004f) }, 24), At(new Vector3(-0.05f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f))),
            };
            k.disc = Join("dischi", disc);

            // ---- the rider (leathers), everything in the frame of the hip joint (torso, head, legs)
            BuildRider(k, inv);

            // ---- shared materials
            k.mFrame = PistaTex.Lit("Telaio", new Color(0.12f, 0.12f, 0.13f), 0.55f, 0.45f);
            k.mMetal = PistaTex.Lit("Alluminio", new Color(0.46f, 0.47f, 0.49f), 0.55f, 0.6f);
            k.mSteel = PistaTex.Lit("Acciaio", new Color(0.55f, 0.52f, 0.48f), 0.55f, 0.6f);
            k.mTitan = PistaTex.Lit("Titanio", new Color(0.55f, 0.55f, 0.58f), 0.72f, 0.9f);
            k.mRubber = PistaTex.Lit("Gomma", new Color(0.035f, 0.035f, 0.038f), 0.12f, 0f);
            k.mSeat = PistaTex.Lit("Sella", new Color(0.06f, 0.06f, 0.065f), 0.18f, 0f);
            k.mGlass = PistaTex.Lit("Vetro", new Color(0.10f, 0.12f, 0.15f), 0.92f, 0.1f);
            k.mGold = PistaTex.Lit("Oro", new Color(0.78f, 0.60f, 0.16f), 0.65f, 0.7f);
            k.mGlove = PistaTex.Lit("Guanto", new Color(0.07f, 0.07f, 0.08f), 0.25f, 0f);
            k.mBoot = PistaTex.Lit("Stivale", new Color(0.06f, 0.06f, 0.065f), 0.30f, 0f);
            k.mVisor = PistaTex.Lit("Visiera", new Color(0.05f, 0.06f, 0.08f), 0.95f, 0.2f);
            kits[style] = k;
            return k;
        }

        static Vector2[] BallProfile(float r, int n = 8)
        {
            var p = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) { float a = Mathf.PI * i / n; p[i] = new Vector2(r * Mathf.Sin(a), -r * Mathf.Cos(a)); }
            return p;
        }

        /// <summary>A ball (a shoulder, a knee, an elbow) of radius r, squashed by 'scale', at a position.</summary>
        static (Mesh mesh, Matrix4x4 at) Ball(float r, Vector3 pos, Vector3 scale, Quaternion rot) => (DesertLathe.Turn("palla", BallProfile(r), 14), At(pos, rot, scale));

        /// <summary>A limb: a smooth tube through control points, with a radius at each of them.</summary>
        static (Mesh mesh, Matrix4x4 at) Limb(Vector3[] ctrl, float[] r, int sides = 12)
        {
            var q = Smooth(ctrl, 4);
            var rr = new float[q.Length];
            for (int i = 0; i < q.Length; i++)
            {
                if (r.Length == 1) { rr[i] = r[0]; continue; }
                float u = i / (float)(q.Length - 1) * (r.Length - 1);
                int a = Mathf.Clamp((int)u, 0, r.Length - 2);
                rr[i] = Mathf.Lerp(r[a], r[a + 1], u - a);
            }
            return (DesertLathe.Tube("arto", q, rr, sides), I);
        }

        static Mesh Unit(string name, float r0, float r1, float r2, float r3)
        {
            return DesertLathe.Tube(name, new[] { Vector3.zero, new Vector3(0f, 0f, 0.33f), new Vector3(0f, 0f, 0.66f), Vector3.forward }, new[] { r0, r1, r2, r3 }, 12);
        }

        static void BuildRider(Kit k, Matrix4x4 inv)
        {
            var lean = Quaternion.Euler(-38f, 0f, 0f);
            // torso: a chest that leans forward from the hip joint, with shoulders and a neck, and a hump on the back of the leathers
            var torso = new List<(Mesh mesh, Matrix4x4 at)>
            {
                (Loft("busto", new[]
                {
                    new Ring(-0.17f, 0.02f, -0.02f, 0.02f, 2.2f), new Ring(-0.15f, 0.095f, -0.06f, 0.07f, 2.4f), new Ring(-0.06f, 0.155f, -0.085f, 0.09f, 2.4f), new Ring(0.06f, 0.165f, -0.10f, 0.10f, 2.4f),
                    new Ring(0.18f, 0.185f, -0.115f, 0.115f, 2.4f), new Ring(0.28f, 0.205f, -0.12f, 0.115f, 2.3f), new Ring(0.36f, 0.17f, -0.10f, 0.09f, 2.3f), new Ring(0.42f, 0.08f, -0.065f, 0.06f, 2.2f),
                    new Ring(0.455f, 0.03f, -0.03f, 0.03f, 2.0f),
                }, 20), At(Vector3.zero, lean)),
            };
            k.torso = Join("busto", torso);
            var trim = new List<(Mesh mesh, Matrix4x4 at)>
            {
                (Loft("gobba", new[] { new Ring(0.02f, 0.05f, 0.08f, 0.11f), new Ring(0.13f, 0.10f, 0.10f, 0.19f), new Ring(0.27f, 0.095f, 0.12f, 0.20f), new Ring(0.37f, 0.05f, 0.11f, 0.16f) }, 16), At(Vector3.zero, lean)),
            };
            foreach (float sd in new[] { -1f, 1f }) trim.Add(Ball(0.068f, lean * new Vector3(sd * 0.20f, 0.0f, 0.31f), Vector3.one, Quaternion.identity));
            k.torsoTrim = Join("gobba e spalle", trim);

            // helmet and visor (their own transform, the head turns into the corner): a shell that is longer than wide, a chin bar and a low spoiler
            k.helmet = Join("casco", new List<(Mesh mesh, Matrix4x4 at)>
            {
                (Loft("casco", new[]
                {
                    new Ring(0.15f, 0.012f, -0.035f, 0.045f, 2.2f), new Ring(0.138f, 0.065f, -0.080f, 0.085f, 2.3f), new Ring(0.108f, 0.105f, -0.112f, 0.115f, 2.4f), new Ring(0.05f, 0.123f, -0.122f, 0.135f, 2.4f),
                    new Ring(-0.03f, 0.127f, -0.122f, 0.145f, 2.4f), new Ring(-0.10f, 0.118f, -0.108f, 0.14f, 2.4f), new Ring(-0.15f, 0.088f, -0.078f, 0.115f, 2.3f), new Ring(-0.19f, 0.03f, -0.04f, 0.07f, 2.1f),
                }, 22), I),
                (Loft("spoiler", new[] { new Ring(-0.11f, 0.03f, 0.125f, 0.138f), new Ring(-0.17f, 0.065f, 0.108f, 0.125f), new Ring(-0.205f, 0.05f, 0.09f, 0.108f) }, 12), I),
            });
            k.visor = Join("visiera", new List<(Mesh mesh, Matrix4x4 at)>
            {
                (Loft("visiera", new[] { new Ring(0.148f, 0.056f, -0.012f, 0.05f), new Ring(0.128f, 0.108f, -0.015f, 0.066f), new Ring(0.08f, 0.129f, -0.018f, 0.072f), new Ring(0.02f, 0.133f, -0.02f, 0.068f) }, 16), At(new Vector3(0f, 0.012f, 0f), Quaternion.identity)),
            });

            // leg (thigh, knee, shin, boot), built for the right side and mirrored by scale
            k.leg = Join("gamba", new List<(Mesh mesh, Matrix4x4 at)>
            {
                Limb(P(0.02f, 0f, 0f, 0.06f, -0.09f, 0.14f, 0.10f, -0.17f, 0.28f), new[] { 0.094f, 0.082f, 0.066f }, 14),
                Limb(P(0.10f, -0.17f, 0.28f, 0.10f, -0.30f, 0.15f, 0.09f, -0.43f, 0.02f), new[] { 0.064f, 0.057f, 0.045f }, 12),
            });
            k.kneeTrim = Join("ginocchio", new List<(Mesh mesh, Matrix4x4 at)> { Ball(0.064f, new Vector3(0.138f, -0.17f, 0.28f), new Vector3(0.65f, 0.95f, 1.05f), Quaternion.identity) });
            k.boot = Join("stivale", new List<(Mesh mesh, Matrix4x4 at)>
            {
                (Loft("stivale", new[]
                {
                    new Ring(-0.11f, 0.04f, -0.05f, 0.06f), new Ring(-0.06f, 0.054f, -0.062f, 0.09f), new Ring(0.04f, 0.052f, -0.052f, 0.06f), new Ring(0.13f, 0.043f, -0.046f, 0.036f), new Ring(0.185f, 0.02f, -0.036f, 0.02f),
                }, 14), At(new Vector3(0.09f, -0.47f, 0.06f), Quaternion.identity)),
                Limb(P(0.09f, -0.40f, 0.02f, 0.09f, -0.47f, 0.0f), new[] { 0.054f, 0.052f }, 10),
            });
            // the glove (a fist round the grip, in the frame of the steering) and the arms: a unit of length that the pose stretches from the shoulder to the elbow and from the elbow to the hand
            k.glove = Join("guanto", new List<(Mesh mesh, Matrix4x4 at)>
            {
                (Loft("guanto", new[] { new Ring(-0.07f, 0.034f, -0.032f, 0.032f), new Ring(-0.035f, 0.052f, -0.042f, 0.042f), new Ring(0.015f, 0.058f, -0.048f, 0.048f), new Ring(0.06f, 0.048f, -0.042f, 0.036f), new Ring(0.082f, 0.022f, -0.024f, 0.022f) }, 12), At(Vector3.zero, Quaternion.identity)),
            });
            k.upperUnit = Unit("braccio", 0.056f, 0.052f, 0.047f, 0.043f);
            k.foreUnit = Unit("avambraccio", 0.046f, 0.042f, 0.037f, 0.032f);
        }

        static Material Copy(Material src, Color c) { var m = new Material(src); m.SetColor("_BaseColor", c); return m; }

        static Transform Part(Transform parent, string name, Mesh mesh, Material mat, List<Renderer> track = null)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.On;
            if (track != null) track.Add(r);
            return g.transform;
        }

        /// <summary>A bike with its rider under 'parent': style 0 superbike, 1 supersport, 2 naked roadster; the paint and the leathers of the livery.</summary>
        public static PistaBikeRig Build(Transform parent, int style, Livery liv)
        {
            var k = Ensure(style);
            var rig = new PistaBikeRig { root = parent };
            rig.paint = PistaTex.Lit("Vernice", liv.main, 0.78f, 0.1f);
            rig.paint2 = PistaTex.Lit("Vernice2", liv.accent, 0.72f, 0.1f);
            rig.plateTex = PistaPlates.Picture(liv.number, liv.main);
            rig.plate = PistaTex.Lit("Targa", Color.white, 0.30f, 0f, rig.plateTex);
            rig.suit = PistaTex.Lit("Tuta", liv.suit, 0.40f, 0f);
            rig.suit2 = PistaTex.Lit("Tuta2", liv.suit2, 0.40f, 0f);
            rig.helmet = PistaTex.Lit("Casco", liv.helmet, 0.85f, 0.05f);
            rig.tailMat = PistaTex.Glow("Fanale", new Color(0.05f, 0.01f, 0.01f), new Color(1f, 0.05f, 0.03f) * 0.5f);
            rig.lensMat = PistaTex.Glow("Faro", new Color(0.12f, 0.12f, 0.12f), new Color(1f, 0.95f, 0.8f) * 0.8f);

            rig.body = new GameObject("Corpo").transform;
            rig.body.SetParent(parent, false);
            Part(rig.body, "Telaio", k.frame, k.mFrame);
            Part(rig.body, "Motore", k.engine, k.mMetal);
            Part(rig.body, "Scarico", k.exhaust, k.mTitan);
            Part(rig.body, "Vernice", k.paint, rig.paint);
            Part(rig.body, "Vernice2", k.paint2, rig.paint2);
            Part(rig.body, "Targhe", k.plates, rig.plate);
            Part(rig.body, "Sella", k.seat, k.mSeat);
            rig.screenParts.Add(Part(rig.body, "Cupolino", k.screen, k.mGlass).GetComponent<Renderer>());
            Part(rig.body, "Specchi", k.mirrors, k.mGlass);
            if (style < 2) Part(rig.body, "Prese", k.nose, k.mFrame);
            var tl = Part(rig.body, "Fanale", k.tail, rig.tailMat);
            tl.localPosition = new Vector3(0f, 0.945f, -0.965f); tl.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            rig.swing = new GameObject("Forcellone").transform;
            rig.swing.SetParent(rig.body, false);
            rig.swing.localPosition = new Vector3(0f, 0.45f, -0.30f);
            var swMesh = Part(rig.swing, "Forcellone", k.swing, k.mFrame);
            swMesh.localPosition = new Vector3(0f, -0.45f, 0.30f);
            rig.rearPivot = new GameObject("Ruota dietro").transform;
            rig.rearPivot.SetParent(rig.swing, false);
            rig.rearPivot.localPosition = AxleR - rig.swing.localPosition;
            Part(rig.rearPivot, "Gomma", k.tyreR, k.mRubber);
            Part(rig.rearPivot, "Cerchio", k.rimR, k.mMetal);
            Part(rig.rearPivot, "Disco", k.disc, k.mSteel).localScale = new Vector3(1f, 0.8f, 0.8f);

            rig.steer = new GameObject("Sterzo").transform;
            rig.steer.SetParent(rig.body, false);
            rig.steer.localPosition = Head;
            rig.steer.localRotation = rig.steerRest = k.steerRot;
            Part(rig.steer, "Forcella", k.fork, k.mGold);
            Part(rig.steer, "Steli", k.forkLow, k.mMetal);
            Part(rig.steer, "Manubrio", k.bars, k.mFrame);
            Part(rig.steer, "Parafango", k.fender, rig.paint2);
            if (style < 2) Part(rig.body, "Fari", k.lensBody, rig.lensMat); else Part(rig.steer, "Fari", k.lens, rig.lensMat);
            rig.frontPivot = new GameObject("Ruota davanti").transform;
            rig.frontPivot.SetParent(rig.steer, false);
            rig.frontBase = k.steerM.inverse.MultiplyPoint3x4(AxleF);
            rig.frontPivot.localPosition = rig.frontBase;
            Part(rig.frontPivot, "Gomma", k.tyreF, k.mRubber);
            Part(rig.frontPivot, "Cerchio", k.rimF, k.mMetal);
            Part(rig.frontPivot, "Dischi", k.disc, k.mSteel);

            // ---- the rider
            rig.hips = new GameObject("Bacino").transform;
            rig.hips.SetParent(rig.body, false);
            rig.hipBase = Hip;
            rig.hips.localPosition = Hip;
            rig.torso = new GameObject("Busto").transform;
            rig.torso.SetParent(rig.hips, false);
            Part(rig.torso, "Tuta", k.torso, rig.suit, rig.riderParts);
            Part(rig.torso, "Gobba e spalle", k.torsoTrim, rig.suit2, rig.riderParts);
            rig.head = new GameObject("Testa").transform;
            rig.head.SetParent(rig.torso, false);
            rig.head.localPosition = Quaternion.Euler(-38f, 0f, 0f) * new Vector3(0f, 0.06f, 0.56f);
            Part(rig.head, "Casco", k.helmet, rig.helmet, rig.riderParts);
            Part(rig.head, "Visiera", k.visor, k.mVisor, rig.riderParts);
            rig.eye = new GameObject("Occhio").transform;
            rig.eye.SetParent(rig.head, false);
            rig.eye.localPosition = new Vector3(0f, 0.02f, 0.09f);
            for (int i = 0; i < 2; i++)
            {
                float sd = i == 0 ? -1f : 1f;
                var leg = new GameObject(i == 0 ? "Gamba sinistra" : "Gamba destra").transform;
                leg.SetParent(rig.hips, false);
                leg.localPosition = new Vector3(sd * 0.08f, 0f, 0f);
                rig.legs[i] = leg;
                var lm = Part(leg, "Coscia e stinco", k.leg, rig.suit2, rig.riderParts); var bm = Part(leg, "Stivale", k.boot, k.mBoot, rig.riderParts); var km = Part(leg, "Ginocchio", k.kneeTrim, rig.suit, rig.riderParts);
                if (i == 0) { lm.localScale = new Vector3(-1f, 1f, 1f); bm.localScale = new Vector3(-1f, 1f, 1f); km.localScale = new Vector3(-1f, 1f, 1f); }
                rig.shoulder[i] = Quaternion.Euler(-38f, 0f, 0f) * new Vector3(sd * 0.19f, 0.03f, 0.33f);
                rig.grip[i] = k.steerM.inverse.MultiplyPoint3x4(new Vector3(sd * 0.30f, style < 2 ? 0.905f : 1.00f, style < 2 ? 0.40f : 0.34f));
                rig.upper[i] = Part(rig.body, "Braccio " + i, k.upperUnit, rig.suit);
                rig.fore[i] = Part(rig.body, "Avambraccio " + i, k.foreUnit, rig.suit2);
                rig.gloves[i] = Part(rig.body, "Guanto " + i, k.glove, k.mGlove);
            }
            // a body of arms parented to the body (not to the steering): their transforms are set in world space every frame
            return rig;
        }
    }
}
