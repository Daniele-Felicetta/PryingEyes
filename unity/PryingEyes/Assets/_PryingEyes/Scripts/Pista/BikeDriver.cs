using System;

namespace PryingEyes.Pista
{
    /// <summary>The rider the computer plays: follows the racing line by pure pursuit (it asks the bike for an exact path curvature, and the bike leans as the physics says it must), holds the planned
    /// speed with throttle and brakes, shifts its line to go around slower bikes, and now and then overcooks a corner. Everything it uses is the same physics the player rides.</summary>
    public sealed class BikeDriver
    {
        public readonly BikeModel bike;
        public readonly Circuit circuit;
        public readonly RacingLine line;

        /// <summary>Fraction of the planned speed this rider carries (0.9 patient .. 0.99 on the limit) and a personal sideways offset from the line (m).</summary>
        public float pace = 0.96f, bias;
        /// <summary>0..1: how often the rider overcooks a corner (runs in too fast and may run wide or fall).</summary>
        public float mistakes = 0.1f;
        /// <summary>Set by the race for the next step: a sideways shift to pass a slower bike (m, positive right) and a speed ceiling (0 = none).</summary>
        public float laneShift, speedLimit;
        /// <summary>The rider this one is going around (-1 none) and the side it chose: kept until the pass is done so the two do not dance.</summary>
        public int commitId = -1;
        public float commitSide;
        /// <summary>Seconds the rider waits after the lights go out.</summary>
        public float reaction = 0.2f;

        /// <summary>Where the bike is on the circuit as of the last Drive.</summary>
        public Proj proj;
        public float s, lateral, targetSpeed;

        int hint = -1;
        Rng rng;
        float mistakeT, cooldown, shiftNow, launchT;
        float prevPlan;

        public BikeDriver(BikeModel bike, Circuit circuit, RacingLine line, int seed)
        {
            this.bike = bike; this.circuit = circuit; this.line = line;
            rng = new Rng(seed);
            bike.autoGears = true; bike.abs = true; bike.tcs = true; bike.antiWheelie = true; bike.antiStoppie = true; bike.leanAssist = true; bike.autoHang = true; bike.autoTuck = true;
        }

        public void Reset()
        {
            hint = -1; mistakeT = 0f; cooldown = 2f; shiftNow = 0f; launchT = 0f; laneShift = 0f; speedLimit = 0f; commitId = -1;
        }

        public BikeInputs Drive(float dt)
        {
            var b = bike;
            var inp = new BikeInputs();
            circuit.Project(b.x, b.z, ref hint, out proj);
            s = proj.s; lateral = proj.d;
            if (b.crashed) return inp;
            launchT += dt;
            float u = PM.Max(b.u, 0f);

            // ---- a rider that is slow to react at the start
            if (launchT < reaction) return inp;

            // ---- steering: aim at a point of the line a little way ahead
            shiftNow += PM.Clamp(laneShift - shiftNow, -2.4f * dt, 2.4f * dt);
            float look = PM.Clamp(5f + 0.40f * u, 7f, 40f);
            float tx, tz;
            line.PointAt(s + look, bias + shiftNow, out tx, out tz);
            float dx = tx - b.x, dz = tz - b.z;
            float sinH = PM.Sin(b.heading), cosH = PM.Cos(b.heading);
            float xr = dx * cosH - dz * sinH, zf = dx * sinH + dz * cosH;
            float chord2 = xr * xr + zf * zf + 0.01f;
            float kappa = zf > 0.5f ? 2f * xr / chord2 : (xr >= 0f ? 1f : -1f) / b.spec.minTurnRadius;
            inp.useCurvature = true; inp.curvature = kappa;

            // ---- speed: the plan ahead, scaled by this rider's pace
            float ahead = PM.Max(6f, u * 0.45f + 6f);
            float vPlan = line.MinPlanAhead(s, ahead);
            // a braking zone is coming: a rider that is not careful may go in too hot
            cooldown -= dt;
            if (mistakeT > 0f) mistakeT -= dt;
            else if (cooldown <= 0f && vPlan < prevPlan - 0.5f && u > 20f && vPlan < u - 6f)
            {
                cooldown = 6f;
                if (rng.Value() < mistakes * 0.35f) mistakeT = 1.6f;
            }
            prevPlan = vPlan;
            float vt = vPlan * pace * (mistakeT > 0f ? 1.13f : 1f);
            // off the asphalt: come back gently
            float hw = circuit.HalfWidthAt(proj);
            if (PM.Abs(lateral) > hw + 0.3f) vt = PM.Min(vt, 17f);
            if (speedLimit > 0f) vt = PM.Min(vt, speedLimit);
            targetSpeed = vt;
            float err = vt - u;
            if (err > -0.4f)
            {
                inp.throttle = PM.Clamp01(0.18f + 0.82f * (err + 0.4f) / 2.2f);
            }
            else
            {
                float brake = PM.Clamp01((-err - 0.4f) / 6f);
                inp.brakeFront = brake * 0.92f;
                inp.brakeRear = brake * 0.25f;
            }
            return inp;
        }
    }
}
