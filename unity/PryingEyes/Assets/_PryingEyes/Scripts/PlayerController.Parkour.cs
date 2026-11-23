using PryingEyes.Deserto;
using UnityEngine;

namespace PryingEyes
{
    public partial class PlayerController
    {
        const float PipeReachHeight = 1.78f, PipeStandOff = 0.34f;
        DesertTowerPipes pipeRoute;
        int pipeIndex;
        enum PipeMove { Catch, Transfer, PullUp, Hang }
        PipeMove pipeMove;
        Vector3 pipeFrom, pipeTo, pipeOver, pipeAcross, pipeOldHandhold, pipeHandhold;
        float pipeTime, pipeDuration, pipeLockout, parkourStow;
        bool airJumpUsed;
        ParkourHands pipeHands;
#if !PE_DISTRIBUTION_BUILD
        internal bool parkourTestJump;
#endif

        public bool OnParkourPipe => pipeRoute != null;
        public bool ParkourHandsBusy => OnParkourPipe || parkourStow > 0.3f;

        void ResetParkour()
        {
            pipeRoute = null;
            airJumpUsed = false;
            pipeLockout = parkourStow = 0f;
            pipeHands?.Hide();
            if (weapon != null) { weapon.ParkourStow = 0f; weapon.ParkourHolding = false; }
        }

        bool TryAirJump(Vector3 wish)
        {
            if (!DesertMode.Active || airJumpUsed || Downed) return false;
            airJumpUsed = true;
            jumpBuf = 0f;
            mantleWindow = 0.38f;
            // Keep take-off momentum and give the second press a small steering impulse.
            Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);
            if (wish.sqrMagnitude > 0f)
                horizontal = Vector3.MoveTowards(horizontal, wish * Mathf.Max(walk, horizontal.magnitude), 1.8f);
            vel = horizontal + Vector3.up * jumpVel * 1.12f;
            FovPunch(2f);
            return true;
        }

        static Vector3 HangBelow(Vector3 bar) => bar + DesertTowerPipes.Outward * PipeStandOff - Vector3.up * PipeReachHeight;

        bool ParkourStep(float dt, bool input, bool jumpPressed, bool dropPressed, float forward, Vector3 wish, Vector3 facing)
        {
            pipeLockout = Mathf.Max(0f, pipeLockout - dt);
            if (!DesertMode.Active || Downed || !cc.enabled)
            {
                pipeRoute = null;
                return false;
            }
            if (pipeRoute == null)
            {
                if (cc.isGrounded) airJumpUsed = false;
                if (!input || Mantling || cc.isGrounded || pipeLockout > 0f || forward < 0.15f || (jumpBuf <= 0f && mantleWindow <= 0f)) return false;
                var route = DesertTowerPipes.FindGrip(transform.position, facing, PipeReachHeight, out int index);
                if (route == null) return false;
                Vector3 bar = route.Bar(index);
                if (!PipeClear(transform.position, HangBelow(bar))) return false;
                pipeRoute = route;
                pipeIndex = index;
                pipeOldHandhold = pipeHandhold = bar;
                StartPipeMove(PipeMove.Catch, HangBelow(bar), 0.16f);
                weapon.BeginParkour();
            }
            else if (input && dropPressed)
            {
                LeavePipe(DesertTowerPipes.Outward * 1.1f + Vector3.down * 1.7f);
                return false;
            }
            else if (input && pipeMove == PipeMove.Hang && jumpBuf > 0f)
            {
                bool up = forward > 0.15f && Vector3.Dot(facing, -DesertTowerPipes.Outward) > 0.25f;
                if (up && pipeIndex + 1 < pipeRoute.Count)
                {
                    Vector3 bar = pipeRoute.Bar(pipeIndex + 1);
                    if (PipeClear(transform.position, HangBelow(bar)))
                    {
                        pipeIndex++;
                        pipeOldHandhold = pipeHandhold;
                        pipeHandhold = bar;
                        StartPipeMove(PipeMove.Transfer, HangBelow(bar), 0.39f);
                    }
                }
                else if (up) TryPipeRoof();
                else
                {
                    Vector3 exit = wish.sqrMagnitude > 0f ? wish : DesertTowerPipes.Outward;
                    exit = Vector3.ProjectOnPlane(exit, DesertTowerPipes.Outward) + DesertTowerPipes.Outward * Mathf.Max(0.48f, Vector3.Dot(exit, DesertTowerPipes.Outward));
                    LeavePipe(exit.normalized * 4.6f + Vector3.up * jumpVel);
                    mantleWindow = 0.4f;
                    return false;
                }
                jumpBuf = 0f;
            }
            else if (jumpPressed && pipeMove != PipeMove.Hang)
            {
                // A press at the end of a transfer lands in the normal jump buffer.
                if (pipeDuration - pipeTime > 0.15f) jumpBuf = 0f;
            }

            vel = Vector3.zero;
            coyote = 0f;
            crouch = slide = false;
            cc.height = heightStand;
            cc.center = Vector3.up * (heightStand * 0.5f);
            if (pipeMove == PipeMove.Hang) return true;
            pipeTime += dt;
            float t = Mathf.Clamp01(pipeTime / pipeDuration);
            Vector3 target = pipeMove == PipeMove.PullUp ? PullUpAt(t) : Vector3.Lerp(pipeFrom, pipeTo, Mathf.SmoothStep(0f, 1f, t));
            cc.Move(target - transform.position);
            if ((transform.position - target).sqrMagnitude > 0.065f * 0.065f)
            {
                LeavePipe(DesertTowerPipes.Outward * 1.1f + Vector3.down);
                return true;
            }
            if (t < 1f) return true;
            if (pipeMove == PipeMove.PullUp)
            {
                LeavePipe(-DesertTowerPipes.Outward * 1.8f + Vector3.down * 0.8f);
                Synth.Step(Surface(), 0.45f);
            }
            else
            {
                pipeMove = PipeMove.Hang;
                airJumpUsed = false;
                Sfx.At(SoundBank.Impact(PryingEyes.Surface.Metal), pipeHandhold, 0.24f, 0.88f);
                Kick(0.95f * GameSettings.Motion, 0f, 12f);
            }
            return true;
        }

        bool PipeClear(Vector3 from, Vector3 to)
        {
            float radius = Mathf.Max(0.05f, cc.radius - cc.skinWidth * 0.5f);
            Vector3 travel = to - from;
            return StandFree(to, heightStand, radius) && !Physics.CapsuleCast(from + Vector3.up * (radius + 0.045f),
                from + Vector3.up * (heightStand - radius), radius, travel.normalized, travel.magnitude, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
        }

        void StartPipeMove(PipeMove move, Vector3 to, float duration)
        {
            pipeMove = move;
            pipeFrom = transform.position;
            pipeTo = to;
            pipeDuration = duration;
            pipeTime = jumpBuf = mantleWindow = 0f;
        }

        void TryPipeRoof()
        {
            // Lift the full capsule above the battlements before moving across the roof edge.
            Vector3 landing = pipeHandhold - DesertTowerPipes.Outward * 2.7f;
            landing.y = pipeRoute.RoofY + 1.95f;
            if (!Physics.Raycast(landing, Vector3.down, out var floor, 2.2f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) ||
                floor.normal.y < 0.75f || Mathf.Abs(floor.point.y - pipeRoute.RoofY) > 0.24f) return;
            Vector3 outside = transform.position;
            outside.y = landing.y;
            Vector3 feet = floor.point + Vector3.up * 0.035f;
            if (!PipeClear(transform.position, outside) || !PipeClear(outside, landing) || !PipeClear(landing, feet)) return;
            pipeOver = outside;
            pipeAcross = landing;
            StartPipeMove(PipeMove.PullUp, feet, 0.94f);
        }

        Vector3 PullUpAt(float t)
        {
            if (t < 0.47f) return Vector3.Lerp(pipeFrom, pipeOver, Mathf.SmoothStep(0f, 1f, t / 0.47f));
            if (t < 0.80f) return Vector3.Lerp(pipeOver, pipeAcross, Mathf.SmoothStep(0f, 1f, (t - 0.47f) / 0.33f));
            return Vector3.Lerp(pipeAcross, pipeTo, Mathf.SmoothStep(0f, 1f, (t - 0.80f) / 0.20f));
        }

        void LeavePipe(Vector3 velocity)
        {
            pipeRoute = null;
            pipeLockout = 0.25f;
            jumpBuf = mantleWindow = 0f;
            vel = velocity;
        }

        void DrawParkourHands(float dt)
        {
            bool attached = OnParkourPipe;
            parkourStow = Mathf.MoveTowards(parkourStow, attached ? 1f : 0f, dt / (attached ? 0.12f : 0.20f));
            weapon.ParkourStow = parkourStow;
            weapon.ParkourHolding = attached;
            if (parkourStow <= 0f) { pipeHands?.Hide(); return; }
            pipeHands ??= ParkourHands.Build(cam, Seats.ViewLayer);
            float progress = Mathf.Clamp01(pipeTime / pipeDuration);
            float pullRelease = pipeMove == PipeMove.PullUp ? Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.09f, 0.35f, progress)) : 1f;
            bool rightLeads = Vector3.Dot(pipeHandhold - pipeOldHandhold, DesertTowerPipes.Across) >= 0f;
            pipeHands?.Tick(pipeOldHandhold, pipeHandhold, rightLeads, pipeMove == PipeMove.Transfer,
                progress, attached, parkourStow * pullRelease, dt);
        }

        bool ParkourPrompt()
        {
            if (!DesertMode.Active) return false;
            string jump = PadControls.Preferred ? "A" : "SALTO";
            if (OnParkourPipe)
            {
                string move = PadControls.Preferred ? "STICK AVANTI" : "AVANTI";
                string action = pipeIndex + 1 < pipeRoute.Count ? "TUBO SUCCESSIVO" : "SALI SUL TETTO";
                hud?.SetPrompt($"{move} + {jump}  {action}   /   {jump}  STACCATI");
                return true;
            }
            var facing = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            if (DesertTowerPipes.FindGrip(transform.position, facing, PipeReachHeight, out _) == null) return false;
            hud?.SetPrompt($"{jump} + AVANTI  AGGRAPPATI");
            return true;
        }
    }
}
