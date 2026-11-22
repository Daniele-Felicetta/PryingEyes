using System;

namespace PryingEyes.Pista
{
    public enum SurfaceKind : byte { Asphalt = 0, Kerb = 1, Apron = 2, Grass = 3, Gravel = 4 }

    /// <summary>How a rider came off. Collision is a hit by another bike (Race.Contacts).</summary>
    public enum CrashKind : byte { None = 0, FrontTuck, FrontLock, Lowside, Highside, Wall, Loop, Endo, Collision }

    /// <summary>How the rider sits on the bike in a corner. LeanBody hangs off the inside: the bike turns in quickly and stays more upright (more ground clearance and a little more grip at deep lean), at the
    /// price of a rider who has more to do. Neutral is the balanced way. LeanBike keeps the body in line with the frame: the bike turns in slowly and steadily and leans further for the same corner.</summary>
    public enum RiderStyle : byte { LeanBody = 0, Neutral = 1, LeanBike = 2 }

    /// <summary>What the ground is like under one wheel: height, how much it grips, how much it drags and shakes, and how it curves vertically along the way (a crest unloads the tires).</summary>
    public struct GroundSample
    {
        public float y;
        public SurfaceKind surface;
        /// <summary>Friction multiplier of the surface (1 = clean asphalt) and rolling-resistance coefficient.</summary>
        public float grip, rolling;
        /// <summary>0 smooth .. 1 very bumpy (kerbs, gravel): shakes the camera and the bike.</summary>
        public float rough;
        /// <summary>Second derivative of the height along the direction of the road (1/m): negative on a crest, positive in a dip.</summary>
        public float vertCurv;
        /// <summary>Banking of the road here (rad), for the picture of the bike only.</summary>
        public float bank;
    }

    /// <summary>The world as the bike physics sees it. 'hint' is a cursor the implementation may use to find the answer fast when the same wheel asks again a moment later.</summary>
    public interface IGround
    {
        void Sample(float x, float z, ref int hint, out GroundSample g);
        /// <summary>True when a disc of 'radius' at (x, z) overlaps a barrier: (nx, nz) is the unit direction that pushes it out, 'depth' how far in it is.</summary>
        bool Barrier(float x, float z, float radius, ref int hint, out float nx, out float nz, out float depth);
    }

    /// <summary>A level, endless, clean asphalt: for the lab's straight-line and corner tests.</summary>
    public sealed class FlatGround : IGround
    {
        public float grip = 1f;
        public void Sample(float x, float z, ref int hint, out GroundSample g)
        {
            g = new GroundSample { y = 0f, surface = SurfaceKind.Asphalt, grip = grip, rolling = 0.015f, rough = 0f, vertCurv = 0f, bank = 0f };
        }
        public bool Barrier(float x, float z, float radius, ref int hint, out float nx, out float nz, out float depth) { nx = nz = depth = 0f; return false; }
    }

    /// <summary>What the rider does in one moment.</summary>
    public struct BikeInputs
    {
        /// <summary>0..1.</summary>
        public float throttle, brakeFront, brakeRear;
        /// <summary>-1 (left) .. 1 (right): the fraction of the tightest turn the bike can hold at this speed and grip.</summary>
        public float steer;
        /// <summary>The rider AI asks for an exact path curvature instead (1/m, positive = right).</summary>
        public bool useCurvature;
        public float curvature;
        /// <summary>-1..1: the rider shifts his body (positive = right), on top of what the automatic body shift does.</summary>
        public float riderLean;
        /// <summary>-1..1: the rider shifts his weight along the bike (positive = forward over the tank, negative = back on the seat).</summary>
        public float riderFwd;
        public bool tuck;
        /// <summary>+1 / -1: shift up or down once (manual gearbox only).</summary>
        public int shift;
    }

    /// <summary>The motorcycle: a rigid frame that leans, an engine with a gearbox, two wheels with slip, tires that share their grip between braking, driving and cornering (the friction ellipse),
    /// weight that moves between the wheels (wheelies and stoppies), air drag, and the road underneath (surfaces, crests, walls). Pure code, deterministic, fixed small steps inside every call, so the
    /// same inputs always give the same ride on every machine.
    ///
    /// The laws, in the order Sub applies them:
    ///  1. The rider asks for a path curvature (a fraction of the tightest turn the bike can hold). The bike leans to the angle that curvature needs at this speed, tan(lean) = v2 k / g, rolling there
    ///     as a second-order system with a limited roll rate; the rider's body shift lets the centre of gravity go beyond the frame's own lean.
    ///  2. The yaw rate follows the lean: r = g tan(lean) / v, the textbook rule of a motorcycle.
    ///  3. The tires must supply m v r of side force. Each tire has mu N of grip, shared with its braking or driving force (the friction ellipse). Too much and the bike slides: the front pushes wide
    ///     (understeer, and a front tuck if it keeps on), the rear steps out (body slip, and a highside if the grip comes back).
    ///  4. Along the bike: engine torque (curve, gears, rev limiter, slipping clutch at launch), wheel inertia, brakes with optional ABS, traction control and anti-wheelie, drag, rolling resistance, slope.
    ///  5. Weight moves from wheel to wheel with the acceleration (m a h / L). If the front gets light the bike rotates about the rear tire (wheelie), and the other way round in a stoppie.
    ///  6. A crest unloads the tires (u2 times the vertical curvature), a dip loads them; kerbs and gravel shake; barriers stop the bike and above a limit knock the rider off.</summary>
    public sealed class BikeModel
    {
        public readonly BikeSpec spec;

        // ---- the rider's aids (the AI uses all of them)
        public bool abs = true, tcs = true, antiWheelie = true, antiStoppie = true, leanAssist = true, autoGears = true, autoHang = true, autoTuck = true;
        /// <summary>How the rider sits in the corners (see RiderStyle).</summary>
        public RiderStyle style = RiderStyle.Neutral;
        /// <summary>0..1, set by the race: how much of the wind this bike is sheltered from by a rider just ahead (the slipstream). It takes up to a third off the drag.</summary>
        public float draft;

        // ---- state (read by the game to draw and to listen)
        /// <summary>The point on the ground halfway between the two tires, the heading (0 = +z, grows toward +x), the frame lean (positive = right) and the visual pitch (positive = nose up).</summary>
        public float x, y, z, heading, lean, pitch;
        /// <summary>Speed along the heading, sideways speed (positive = right), yaw rate.</summary>
        public float u, vl, yawRate;
        public float rpm;
        public int gear = 1;
        /// <summary>The rider's body shift (-1..1, positive = right) and how tucked he is (0..1).</summary>
        public float hang, tuckAmt;
        /// <summary>The rider's weight along the bike (-1 back .. 1 forward): it moves the whole centre of gravity by the rider's share of the mass.</summary>
        public float body;
        public float omegaF, omegaR;
        public float forkCompress, shockCompress;
        /// <summary>The handlebar angle, for the picture (positive = right), and the wheelie (positive) or stoppie (negative) angle.</summary>
        public float steerAngle, freePitch;
        public float leanRate;

        // ---- telemetry
        public bool absActive, tcsActive, frontSliding, rearSliding, scraping, shifting, limiter, wheelieAssist;
        public float latG, longG, rough, throttleOut;
        public SurfaceKind surfaceF, surfaceR;
        public float loadFront, loadRear, slipFront, slipRear;
        public bool crashed;
        public CrashKind crashKind;
        public float crashTime, crashSpeed;
        public float vxWorld, vzWorld;

        // ---- internals
        const float MaxSub = 1f / 300f, U0 = 1.2f, BarrierRadius = 0.36f;
        /// <summary>How far the rider moves his own body along the seat (m) and how much of the drag the slipstream can take away.</summary>
        public const float BodyTravel = 0.25f, DraftDrag = 0.32f;
        const float CrashWallSpeed = 5.6f;
        int hintF, hintR, hintC, hintB1, hintB2, hintB3;
        float axSm, fxF, fxR, fyFrontPrev, fyRearPrev, capSm = 9000f;
        float tuckT, frontLockT, slideT, slipPeak, shiftT, shiftBlipT;
        /// <summary>Seconds left in which a hit by another bike is still shaking the bike (a sideways speed that the grip is about to take away is not a slide), and whether the rear slid on loose ground (coming back onto the
        /// asphalt after that is not a highside).</summary>
        float shoveT;
        bool slideLoose;
        int pendingGear;
        float tcsFactor = 1f, absFactor = 1f;
        float freeRate, crashSpin, crashLeanTarget, simTime;
        Rng rng;

        public BikeModel(BikeSpec spec, int seed = 1)
        {
            this.spec = spec;
            rng = new Rng(seed);
            Reset(0f, 0f, 0f, 0f);
        }

        public float SpeedKmh { get { return u * 3.6f; } }

        /// <summary>Put the bike upright and still at a place, ready to go (first gear, engine idling).</summary>
        public void Reset(float px, float pz, float head, float py)
        {
            x = px; z = pz; y = py; heading = head;
            lean = pitch = leanRate = 0f; u = vl = yawRate = 0f; vxWorld = vzWorld = 0f;
            gear = 1; pendingGear = 1; rpm = spec.idleRpm; shiftT = 0f; hang = 0f; tuckAmt = 0f; body = 0f; draft = 0f;
            omegaF = omegaR = 0f; forkCompress = shockCompress = 0f; steerAngle = 0f; freePitch = freeRate = 0f;
            absActive = tcsActive = frontSliding = rearSliding = scraping = shifting = limiter = wheelieAssist = false;
            latG = longG = rough = throttleOut = 0f;
            crashed = false; crashKind = CrashKind.None; crashTime = 0f; crashSpeed = 0f;
            axSm = fxF = fxR = fyFrontPrev = fyRearPrev = 0f; capSm = 9000f;
            tuckT = frontLockT = slideT = slipPeak = 0f; shoveT = 0f; slideLoose = false;
            tcsFactor = absFactor = 1f;
            hintF = hintR = hintC = hintB1 = hintB2 = hintB3 = 0;
        }

        // ================================================================== stepping
        /// <summary>Advance the bike by 'dt' seconds in small fixed steps.</summary>
        public void Step(float dt, IGround ground, BikeInputs inp)
        {
            if (dt <= 0f) return;
            int n = (int)Math.Ceiling(dt / MaxSub);
            if (n < 1) n = 1; else if (n > 40) n = 40;
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                simTime += h;
                if (crashed) SubCrashed(h, ground);
                else Sub(h, ground, ref inp);
                inp.shift = 0;
            }
        }

        /// <summary>Gravel and grass: the grounds where the grip is low and the rider is given time before the bike goes down.</summary>
        static bool Loose(SurfaceKind k) { return k == SurfaceKind.Gravel || k == SurfaceKind.Grass; }

        static float Ellipse(float cap, float used)
        {
            float v = cap * cap - used * used;
            return v > 0f ? PM.Sqrt(v) : 0f;
        }

        /// <summary>One wheel for one small step, implicit in the wheel speed so a light wheel with a stiff tire stays stable. Returns the tire force on the bike.</summary>
        static float WheelStep(ref float omega, float inertia, float torque, float brake, float radius, float speed, float cap, float peak, float h, out float slip)
        {
            float vref = PM.Max(PM.Abs(speed), 3f);
            float s0 = (omega * radius - speed) / vref / peak;
            float a = PM.Abs(s0);
            float curve = PM.TireCurve(a);
            float f0 = cap * (s0 < 0f ? -curve : curve);
            // slope of the tire force against the wheel speed, kept positive (past the peak the secant is nearly flat) so the implicit step stays a contraction
            float dcurve = 1.5f * 1.7320508f / (1f + 3f * a * a) * (float)Math.Cos(1.5 * Math.Atan(1.7320508 * a));
            float c = PM.Max(dcurve, 0.08f) * cap / peak / vref * radius;
            float sg = PM.Clamp(omega / 1.5f, -1f, 1f);
            float tq = torque - brake * sg;
            float w = (inertia * omega + h * (tq - radius * f0 + radius * c * omega)) / (inertia + h * radius * c);
            if (w < 0f) w = 0f;
            omega = w;
            // the force that goes to the bike is the one at the slip the wheel ends up with
            float s = (omega * radius - speed) / vref / peak;
            slip = s;
            float curveEnd = PM.TireCurve(PM.Abs(s));
            return cap * (s < 0f ? -curveEnd : curveEnd);
        }

        void Sub(float h, IGround ground, ref BikeInputs inp)
        {
            var sp = spec;
            float L = sp.wheelbase, m = sp.Mass;
            float lr = sp.cgFromRear + body * (sp.riderMass / m) * BodyTravel, lf = L - lr, R = sp.wheelRadius;
            float sinH = PM.Sin(heading), cosH = PM.Cos(heading), half = L * 0.5f;

            // ---- the ground under the two tires and the middle
            GroundSample gF, gR, gC;
            ground.Sample(x + sinH * half, z + cosH * half, ref hintF, out gF);
            ground.Sample(x - sinH * half, z - cosH * half, ref hintR, out gR);
            ground.Sample(x, z, ref hintC, out gC);
            y = gC.y;
            surfaceF = gF.surface; surfaceR = gR.surface;
            float groundPitch = PM.Atan2(gF.y - gR.y, L);
            float cosP = PM.Cos(groundPitch);
            // hanging off keeps the tires nearer the middle of their profile, sitting in line with the bike pushes them to the shoulder: a few per cent of grip, only at deep lean
            float leanEdge = PM.Clamp01((PM.Abs(lean) / sp.frameLeanMax - 0.6f) / 0.4f);
            float styleMu = 1f + (style == RiderStyle.LeanBody ? 0.025f : style == RiderStyle.LeanBike ? -0.04f : 0f) * leanEdge;
            float muF = sp.muFront * gF.grip * styleMu, muR = sp.muRear * gR.grip * styleMu;
            rough = PM.Max(gF.rough, gR.rough);
            // loose ground (gravel, grass) is a place to lose time, not to fall: a rider who has gone wide is given time to stand the bike up (see 'falling off'). The lean assist gives it all, no assist most of it.
            bool loose = Loose(gF.surface) || Loose(gR.surface);
            float forgive = loose ? (leanAssist ? 1f : 0.8f) : 0f;
            if (shoveT > 0f) shoveT -= h;

            float uu = PM.Max(u, 0f);

            // ---- the rider's body: tuck and shift
            bool wantTuck = inp.tuck || (autoTuck && uu > 48f && PM.Abs(lean) < 0.14f && inp.throttle > 0.7f && inp.brakeFront < 0.05f);
            tuckAmt += ((wantTuck ? 1f : 0f) - tuckAmt) * PM.Ease(4f, h);
            float hcg = PM.Lerp(sp.cgHeight, sp.cgHeightTuck, tuckAmt);
            body += (PM.Clamp(inp.riderFwd, -1f, 1f) - body) * PM.Ease(4.5f, h);

            // ---- loads on the tires: the weight, a crest or a dip, and the transfer that the acceleration causes
            float loadF = PM.Clamp(1f + uu * uu * gC.vertCurv / PM.G, 0.06f, 2.2f);
            float nTot = m * PM.G * cosP * loadF;
            float transfer = m * axSm * hcg / L;
            float nfRaw = nTot * lr / L - transfer, nrRaw = nTot * lf / L + transfer;
            float nf, nr;
            // the free pitch: front light and rising (wheelie) or rear light and rising (stoppie)
            if (freePitch == 0f && freeRate == 0f)
            {
                if (nfRaw < 0f && axSm > 0.5f) { freePitch = 0.004f; freeRate = 0.02f; }
                else if (nrRaw < 0f && axSm < -0.5f) { freePitch = -0.004f; freeRate = -0.02f; }
            }
            if (freePitch != 0f)
            {
                float th = PM.Abs(freePitch);
                float pivot = freePitch > 0f ? lr : lf;           // distance from the CG to the tire the bike turns about
                float xx = pivot * PM.Cos(th) - hcg * PM.Sin(th);
                float zz = pivot * PM.Sin(th) + hcg * PM.Cos(th);
                float inertiaAbout = m * (pivot * pivot + hcg * hcg) + 14f;
                float accelAlong = freePitch > 0f ? axSm : -axSm;
                float torque = m * accelAlong * zz - m * PM.G * xx;       // positive = further up
                freeRate += torque / inertiaAbout * h * (freePitch > 0f ? 1f : -1f);
                freePitch += freeRate * h;
                bool up = freePitch > 0f;
                if ((up && freePitch <= 0f) || (!up && freePitch >= 0f) || (freeRate * (up ? 1f : -1f) < 0f && th < 0.006f))
                {
                    // back on both wheels: the fork or the shock takes the landing
                    float land = PM.Abs(freeRate);
                    if (up) forkCompress += PM.Clamp(land * 0.05f, 0f, 0.06f); else shockCompress += PM.Clamp(land * 0.05f, 0f, 0.05f);
                    freePitch = 0f; freeRate = 0f;
                    nf = nfRaw < 0f ? 0f : nfRaw; nr = nTot - nf;
                }
                else
                {
                    if (PM.Abs(freePitch) > 1.25f && !crashed) { BeginCrash(freePitch > 0f ? CrashKind.Loop : CrashKind.Endo, h); return; }
                    if (freePitch > 0f) { nf = 0f; nr = nTot; } else { nr = 0f; nf = nTot; }
                }
            }
            else { nf = nfRaw; nr = nTot - nfRaw; if (nf < 0f) nf = 0f; if (nr < 0f) nr = 0f; }
            loadFront = nf; loadRear = nr;

            // ---- the gearbox: shifts, automatic or manual
            if (shiftT > 0f)
            {
                shiftT -= h;
                if (shiftT <= 0f) { gear = pendingGear; shifting = false; }
            }
            float wheelRpm = omegaR * sp.TotalRatio(gear) * 30f / PM.Pi;
            if (shiftT <= 0f)
            {
                if (!autoGears)
                {
                    if (inp.shift > 0 && gear < sp.GearCount) StartShift(gear + 1);
                    else if (inp.shift < 0 && gear > 1) StartShift(gear - 1);
                }
                else
                {
                    float up = sp.upshiftRpm * PM.Lerp(0.80f, 1f, PM.Clamp01(inp.throttle * 1.4f));
                    if (wheelRpm > up && gear < sp.GearCount) StartShift(gear + 1);
                    else if (gear > 1)
                    {
                        float rpmAfter = wheelRpm * sp.TotalRatio(gear - 1) / sp.TotalRatio(gear);
                        float down = sp.downshiftRpm * (inp.brakeFront > 0.3f ? 1.5f : 1f);
                        if (wheelRpm < down && rpmAfter < sp.redlineRpm * 0.86f) StartShift(gear - 1);
                    }
                }
            }
            shifting = shiftT > 0f;

            // ---- throttle after the aids
            float thr = PM.Clamp01(inp.throttle);
            float aw = 1f;
            wheelieAssist = false;
            if (antiWheelie)
            {
                float staticFront = nTot * lr / L;
                float frontShare = staticFront > 1f ? nfRaw / staticFront : 1f;
                aw = PM.Clamp01((frontShare - 0.03f) / 0.25f);
                if (freePitch > 0.09f) aw = PM.Min(aw, PM.Clamp01(1f - (freePitch - 0.09f) / 0.10f));
                wheelieAssist = aw < 0.97f && thr > 0.05f;
            }
            float thrEff = thr * aw * (tcs ? tcsFactor : 1f);
            throttleOut = thrEff;

            // ---- engine speed and torque
            float gRatio = sp.TotalRatio(gear);
            float floorRpm = gear <= 2 ? PM.Lerp(sp.idleRpm, sp.launchRpm, thr) : sp.idleRpm * 1.1f;
            bool coupled = !shifting && gear >= 1 && wheelRpm >= floorRpm - 20f;
            if (shifting)
            {
                float target = PM.Max(sp.idleRpm, omegaR * sp.TotalRatio(pendingGear) * 30f / PM.Pi);
                if (shiftBlipT > 0f) { shiftBlipT -= h; target = PM.Max(target, omegaR * sp.TotalRatio(pendingGear) * 30f / PM.Pi); }
                rpm += (target - rpm) * PM.Ease(28f, h);
            }
            else if (coupled) rpm = wheelRpm;
            else rpm += (PM.Max(floorRpm, wheelRpm) - rpm) * PM.Ease(14f, h);
            rpm = PM.Clamp(rpm, sp.idleRpm * 0.9f, sp.limiterRpm + 600f);
            float te = sp.TorqueAt(rpm) * thrEff - sp.engineBrake * (rpm / 10000f) * (1f - thrEff);
            limiter = rpm >= sp.limiterRpm;
            if (limiter && te > 0f) te = 0f;
            if (te < -sp.slipperTorque) te = -sp.slipperTorque;
            if (!coupled && te < 0f) te = 0f;            // the clutch is slipping or open: nothing brakes the wheel through the engine
            float tWheel = shifting || gear < 1 ? 0f : te * gRatio * sp.driveEfficiency;
            float iRear = sp.wheelInertiaRear + (coupled ? sp.engineInertia * gRatio * gRatio * sp.driveEfficiency : 0f);

            // ---- brakes, with ABS: back off when the front wheel is near its slip peak
            float peak = sp.slipPeakRatio;
            absActive = false;
            float brakeF = PM.Clamp01(inp.brakeFront), brakeR = PM.Clamp01(inp.brakeRear);
            float sF = slipFront / peak;
            if (abs && brakeF > 0.02f)
            {
                float over = PM.Clamp01((PM.Abs(sF) - 0.85f) / 0.35f);
                float target = 1f - over;
                absFactor += (target - absFactor) * PM.Ease(60f, h);
                absActive = over > 0.05f;
            }
            else absFactor = 1f;
            // anti-stoppie: let go of the front brake as the rear tire unloads (modern bikes do it with the rear-lift control)
            float liftFactor = 1f;
            if (antiStoppie)
            {
                float staticRear = nTot * lf / L;
                float rearShare = staticRear > 1f ? nrRaw / staticRear : 1f;
                liftFactor = PM.Clamp01((rearShare - 0.03f) / 0.20f);
                if (freePitch < -0.04f) liftFactor = PM.Min(liftFactor, PM.Clamp01(1f + (freePitch + 0.04f) / 0.08f));
            }
            float brakeTorqueF = brakeF * sp.brakeFront * (abs ? absFactor : 1f) * liftFactor;
            float brakeTorqueR = brakeR * sp.brakeRear * (abs ? PM.Clamp01(1f - PM.Max(0f, (slipRear < 0f ? -slipRear : 0f) / peak - 0.9f)) : 1f);

            // ---- the tires along the bike, with what is left of their grip after the side force of the last step
            float capXf = Ellipse(muF * nf, fyFrontPrev);
            float capXr = Ellipse(muR * nr, fyRearPrev);
            float sRear, sFront;
            float newFxR = WheelStep(ref omegaR, iRear, tWheel, brakeTorqueR, R, u, capXr, peak, h, out sRear);
            float newFxF = WheelStep(ref omegaF, sp.wheelInertiaFront, 0f, brakeTorqueF, R, u, capXf, peak, h, out sFront);
            slipRear = sRear * peak; slipFront = sFront * peak;
            fxR = newFxR; fxF = newFxF;
            // traction control: ease the throttle when the rear spins past its peak (it moves slowly: a hint, not a rule)
            float spinRatio = PM.Max(0f, sRear);
            float tcsTarget = PM.Clamp01(1f - (spinRatio - 0.9f) / 0.4f);
            tcsFactor += (tcsTarget - tcsFactor) * PM.Ease(30f, h);
            tcsActive = tcs && tcsFactor < 0.95f && thr > 0.05f;

            // ---- along the bike: drag, rolling, slope
            float cda = PM.Lerp(sp.cdA, sp.cdATuck, tuckAmt) * (1f - DraftDrag * PM.Clamp01(draft));
            float drag = 0.5f * 1.2f * cda * uu * uu;
            float rollF = uu > 0.05f ? gC.rolling * nTot : 0f;
            float slopeF = -m * PM.G * PM.Sin(groundPitch);
            float uDot = (fxR + fxF - drag - rollF + slopeF) / m + vl * yawRate;
            u += uDot * h;
            if (u < 0f) u = 0f;
            axSm += (uDot - axSm) * PM.Ease(14f, h);
            longG = axSm / PM.G;

            // ---- grip left over for cornering (friction ellipse at each tire)
            float capF = Ellipse(muF * nf, fxF), capR = Ellipse(muR * nr, fxR);
            capSm += (capF + capR - capSm) * PM.Ease(18f, h);
            float shareF = nTot > 1f ? nf / nTot : 0.5f;

            // ---- the lean the rider asks for
            // the weight that tips the bike over (and that the tires feel) is the real one scaled by the slope and by crests and dips: gEff
            float gEff = PM.G * cosP * loadF;
            float uEff2 = uu * uu + U0 * U0;
            float hangMaxEff = sp.hangMax * (style == RiderStyle.LeanBody ? 1.55f : style == RiderStyle.LeanBike ? 0.25f : 1f);
            float phiLimit = sp.frameLeanMax + PM.Abs(hang) * hangMaxEff;
            float phiGrip = PM.Atan(0.96f * capSm / (nTot + 1f));
            // on loose ground nobody can lean the bike further than the ground carries: the front pushes wide instead (with the assist this holds on every ground)
            float phiMax = leanAssist || loose ? PM.Min(phiLimit, phiGrip) : phiLimit;
            float kappaMax = PM.Min(1f / sp.minTurnRadius, gEff * PM.Tan(phiMax) / uEff2);
            float kappaCmd = inp.useCurvature ? PM.Clamp(inp.curvature, -kappaMax, kappaMax) : PM.Clamp(inp.steer, -1f, 1f) * kappaMax;
            float phiCgCmd = PM.Atan(uEff2 * kappaCmd / gEff);
            float autoGain = style == RiderStyle.LeanBody ? 1.05f : style == RiderStyle.LeanBike ? 0.15f : 0.9f;
            float hangT = PM.Clamp(inp.riderLean + (autoHang && uu > 9f ? PM.Clamp(phiCgCmd / 0.70f, -1f, 1f) * autoGain : 0f), -1f, 1f);
            hang += (hangT - hang) * PM.Ease(5.5f, h);
            float dHang = hang * hangMaxEff;
            float phiCmd = phiCgCmd - dHang;
            float vk = PM.Clamp01(uu / 80f);
            // the body that goes over first makes the bike fall into the turn faster; the body that stays put makes it slow and steady
            float wn = PM.Lerp(sp.rollOmegaLow, sp.rollOmegaHigh, vk) * (style == RiderStyle.LeanBody ? 1.10f : style == RiderStyle.LeanBike ? 0.90f : 1f);
            float rateMax = PM.Lerp(sp.rollRateLow, sp.rollRateHigh, vk) * (style == RiderStyle.LeanBody ? 1.12f : style == RiderStyle.LeanBike ? 0.88f : 1f);
            // the moment the front lets go on loose ground the bike stands up, much faster than a rider leans it into a corner: a front that pushes wide on gravel takes the lean out of the bike by itself, and the assist helps it
            if (loose && frontSliding) rateMax *= leanAssist ? 3f : 2f;
            leanRate += (wn * wn * (phiCmd - lean) - 2f * 0.85f * wn * leanRate) * h;
            leanRate = PM.Clamp(leanRate, -rateMax, rateMax);
            lean += leanRate * h;
            float leanCap = sp.frameLeanMax + 0.03f;
            if (lean > leanCap) { lean = leanCap; if (leanRate > 0f) leanRate = 0f; }
            else if (lean < -leanCap) { lean = -leanCap; if (leanRate < 0f) leanRate = 0f; }
            scraping = PM.Abs(lean) > sp.frameLeanMax * 0.92f && uu > 8f;
            // kerbs and gravel make the bike shake a little
            if (rough > 0.25f && uu > 8f) leanRate += (rng.Value() - 0.5f) * 0.9f * rough * h * 60f * 0.25f;

            // ---- yaw rate from the lean
            float phiCg = lean + dHang;
            float rk = gEff * PM.Tan(phiCg) * uu / uEff2;
            float rMax = uu / sp.minTurnRadius;
            rk = PM.Clamp(rk, -rMax, rMax);

            // ---- does the grip hold the turn?
            float fyReq = m * uu * rk;
            float fyCap = capF + capR;
            float sat = PM.Abs(fyReq) / PM.Max(fyCap, 60f);
            float rhoF = PM.Abs(fyReq) * shareF / PM.Max(capF, 25f);
            float rhoR = PM.Abs(fyReq) * (1f - shareF) / PM.Max(capR, 25f);
            if (nf <= 1f) rhoF = 0f;
            if (nr <= 1f) rhoR = 0f;
            float fyNet = sat <= 1f ? fyReq : (fyReq < 0f ? -1f : 1f) * fyCap * 0.92f;
            bool slideFront = sat > 1f && rhoF >= rhoR;
            bool slideRear = sat > 1f && rhoR > rhoF;
            frontSliding = slideFront; rearSliding = slideRear;
            float r;
            if (sat <= 1f)
            {
                r = rk;
                vl *= PM.Exp(-h / 0.07f);
            }
            else if (slideFront)
            {
                r = fyNet / (m * PM.Max(uu, 1f));
                vl *= PM.Exp(-h / 0.2f);
            }
            else
            {
                r = rk;
                vl += (fyNet - m * uu * r) / m * h;
                vl *= PM.Exp(-h * 0.35f);
            }
            fyFrontPrev = PM.Abs(fyNet) * shareF;
            fyRearPrev = PM.Abs(fyNet) * (1f - shareF);
            latG = uu * r / PM.G;

            // ---- falling off
            float beta = PM.Atan2(vl, PM.Max(uu, 1f));
            if (slideRear) { slideT += h; slipPeak = PM.Max(slipPeak, PM.Abs(beta)); if (loose) slideLoose = true; }
            else
            {
                // coming back onto the asphalt after a slide on the gravel or the grass is a wobble, not the highside of a slide that began on the road
                if (slideT > 0.12f && slipPeak > 0.16f && sat < 0.92f && PM.Abs(lean) > 0.18f && uu > 8f)
                {
                    if (slideLoose) leanRate += (rng.Value() - 0.5f) * 1.2f;
                    else { BeginCrash(CrashKind.Highside, h); return; }
                }
                slideT = PM.Max(0f, slideT - h * 2f); slipPeak *= PM.Exp(-h * 3f);
                if (slideT <= 0f) slideLoose = false;
            }
            // the front pushing wide on loose ground tucks slowly (and the bike is already standing up): a good deal longer than on the road, longest with the assist
            if (slideFront) tuckT += (rhoF - 1f) * h * (0.9f + 4.5f * PM.Abs(lean)) * (1f - 0.7f * forgive); else tuckT = PM.Max(0f, tuckT - 1.5f * h);
            if (tuckT > 0.085f * (1f + 3f * forgive) && uu > 5f) { BeginCrash(CrashKind.FrontTuck, h); return; }
            // a hit by another bike leaves a sideways speed that the tires are about to take away: it is not yet a slide
            if (PM.Abs(beta) > 0.32f + 0.20f * forgive + (shoveT > 0f ? 0.22f : 0f) && uu > 6f) { BeginCrash(CrashKind.Lowside, h); return; }
            // a locked front wheel in a turn, or held for long, folds
            if (slipFront < -0.55f * 1f && uu > 6f)
            {
                frontLockT += h * (PM.Abs(lean) > 0.10f ? 4f : 1f);
                if (frontLockT > 0.9f) { BeginCrash(CrashKind.FrontLock, h); return; }
            }
            else frontLockT = PM.Max(0f, frontLockT - h * 2f);

            // ---- move
            float hm = heading + 0.5f * r * h;
            float sm = PM.Sin(hm), cm = PM.Cos(hm);
            vxWorld = uu * sm + vl * cm; vzWorld = uu * cm - vl * sm;
            x += vxWorld * h; z += vzWorld * h;
            heading += r * h;
            yawRate = r;

            // ---- barriers
            if (Barriers(ground, h)) { BeginCrash(CrashKind.Wall, h); return; }

            // ---- the springs: dive, squat, and the pitch and steering the picture needs
            float nf0 = m * PM.G * lr / L, nr0 = m * PM.G * lf / L;
            float fkT = PM.Clamp((nf - nf0) / sp.forkRate, -0.03f, 0.16f), shT = PM.Clamp((nr - nr0) / sp.shockRate, -0.03f, 0.10f);
            forkCompress += (fkT - forkCompress) * PM.Ease(13f, h);
            shockCompress += (shT - shockCompress) * PM.Ease(13f, h);
            pitch = groundPitch + PM.Atan((shockCompress - forkCompress) / L) + freePitch;
            steerAngle = PM.Atan(L * r / PM.Max(uu, 1.5f)) - 0.08f * leanRate / (1f + uu / 15f);
        }

        void StartShift(int toGear)
        {
            pendingGear = toGear;
            shiftT = spec.shiftTime;
            shiftBlipT = toGear < gear ? spec.shiftTime : 0f;
            shifting = true;
        }

        // ================================================================== barriers and crashes
        bool Barriers(IGround ground, float h)
        {
            float sinH = PM.Sin(heading), cosH = PM.Cos(heading), half = spec.wheelbase * 0.5f;
            for (int k = 0; k < 3; k++)
            {
                float off = k == 0 ? half : k == 1 ? -half : 0f;
                float px = x + sinH * off, pz = z + cosH * off;
                float nx, nz, depth;
                int hint = k == 0 ? hintB1 : k == 1 ? hintB2 : hintB3;
                bool b = ground.Barrier(px, pz, BarrierRadius, ref hint, out nx, out nz, out depth);
                if (k == 0) hintB1 = hint; else if (k == 1) hintB2 = hint; else hintB3 = hint;
                if (!b) continue;
                x += nx * depth; z += nz * depth;
                float vx = u * sinH + vl * cosH, vz = u * cosH - vl * sinH;
                float vn = vx * nx + vz * nz;
                if (vn >= 0f) continue;
                float impact = -vn;
                if (impact > crashSpeed) crashSpeed = impact;
                if (impact > CrashWallSpeed) return true;
                // a scrape: lose the part of the speed into the wall and a little along it, and swing away from it
                float tx = vx - vn * nx, tz = vz - vn * nz;
                float keep = PM.Clamp(1f - 0.045f * impact, 0.5f, 1f);
                vx = tx * keep; vz = tz * keep;
                u = vx * sinH + vz * cosH; vl = vx * cosH - vz * sinH;
                if (u < 0f) u = 0f;
                heading += PM.Clamp(impact * 0.012f, 0f, 0.05f) * (nx * cosH - nz * sinH < 0f ? 1f : -1f);
                leanRate += (rng.Value() - 0.5f) * 0.6f;
            }
            return false;
        }

        /// <summary>Another bike touched this one (see Race.Contacts): the bike is moved out of the other by (dx, dz) metres, its velocity changes by (dvx, dvz) metres a second in the world, its heading is twisted by
        /// 'twist' radians and the frame is shaken by 'wobble'. A bike that has fallen is left as it is.</summary>
        public void Shove(float dx, float dz, float dvx, float dvz, float twist, float wobble)
        {
            if (crashed) return;
            x += dx; z += dz;
            float sinH = PM.Sin(heading), cosH = PM.Cos(heading);
            u += dvx * sinH + dvz * cosH; vl += dvx * cosH - dvz * sinH;
            if (u < 0f) u = 0f;
            heading += twist;
            leanRate += (rng.Value() - 0.5f) * wobble;
            shoveT = 0.45f;
        }

        /// <summary>A hard hit by another bike: the rider is down, sliding on with the speed the hit left him.</summary>
        public void ContactCrash() { BeginCrash(CrashKind.Collision, 0f); }

        void BeginCrash(CrashKind kind, float h)
        {
            if (crashed) return;
            crashed = true; crashKind = kind; crashTime = 0f;
            float sinH = PM.Sin(heading), cosH = PM.Cos(heading);
            vxWorld = PM.Max(u, 0f) * sinH + vl * cosH; vzWorld = PM.Max(u, 0f) * cosH - vl * sinH;
            if (kind == CrashKind.Wall)
            {
                vxWorld *= 0.45f; vzWorld *= 0.45f;
            }
            crashSpin = yawRate * 0.7f + (rng.Value() - 0.5f) * 1.2f;
            float side = lean >= 0f ? 1f : -1f;
            if (kind == CrashKind.Highside) side = -side;
            crashLeanTarget = side * 1.38f;
            if (kind == CrashKind.Loop || kind == CrashKind.Endo) crashLeanTarget = lean;
            frontSliding = rearSliding = false; absActive = tcsActive = false;
            freePitch = kind == CrashKind.Loop ? 1.3f : kind == CrashKind.Endo ? -1.3f : 0f; freeRate = 0f;
        }

        void SubCrashed(float h, IGround ground)
        {
            crashTime += h;
            GroundSample g;
            ground.Sample(x, z, ref hintC, out g);
            y = g.y;
            float speed = PM.Sqrt(vxWorld * vxWorld + vzWorld * vzWorld);
            float mu = g.surface == SurfaceKind.Gravel ? 1.05f : g.surface == SurfaceKind.Grass ? 0.55f : g.surface == SurfaceKind.Kerb ? 0.75f : 0.62f;
            float dec = mu * PM.G * h;
            float ns = PM.Max(0f, speed - dec);
            if (speed > 1e-3f) { float k = ns / speed; vxWorld *= k; vzWorld *= k; }
            x += vxWorld * h; z += vzWorld * h;
            heading += crashSpin * h;
            crashSpin *= PM.Exp(-h * 0.9f);
            lean += (crashLeanTarget - lean) * PM.Ease(9f, h);
            leanRate = 0f;
            float sinH = PM.Sin(heading), cosH = PM.Cos(heading);
            u = vxWorld * sinH + vzWorld * cosH; vl = vxWorld * cosH - vzWorld * sinH;
            omegaR *= PM.Exp(-h * 2.5f); omegaF *= PM.Exp(-h * 2.5f);
            rpm += (spec.idleRpm - rpm) * PM.Ease(5f, h);
            yawRate = crashSpin; latG = 0f; longG = -mu;
            throttleOut = 0f; tcsActive = absActive = false; limiter = false;
            // sliding into a barrier stops the bike
            float nx, nz, depth;
            if (ground.Barrier(x, z, BarrierRadius, ref hintB3, out nx, out nz, out depth))
            {
                x += nx * depth; z += nz * depth;
                float vn = vxWorld * nx + vzWorld * nz;
                if (vn < 0f) { vxWorld -= vn * nx * 1.2f; vzWorld -= vn * nz * 1.2f; vxWorld *= 0.8f; vzWorld *= 0.8f; }
            }
            pitch += (0f - pitch) * PM.Ease(6f, h);
        }

        /// <summary>The slide is over: the bike has stopped (or it has been long enough) and the game may put the rider back on the track.</summary>
        public bool CrashOver { get { return crashed && ((crashTime > 0.8f && PM.Sqrt(vxWorld * vxWorld + vzWorld * vzWorld) < 0.6f) || crashTime > 5f); } }
    }
}
