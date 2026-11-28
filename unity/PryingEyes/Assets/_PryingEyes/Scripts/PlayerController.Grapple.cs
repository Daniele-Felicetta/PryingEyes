using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes
{
    public partial class PlayerController
    {
        internal const float GrappleRange = 72f;
        const float ShotSpeed = 145f, ReturnSpeed = 120f;
        enum HookPhase : byte { Ready, Flying, Attached, Returning }

        HookPhase hookPhase;
        Collider hookSurface;
        Vector3 hookLocalPoint, hookTip, hookFrom;
        float hookTravel, hookBlocked;
        GrappleVisual hookVisual;
        bool launchGrapple;

        bool GrappleLive => hookPhase == HookPhase.Flying || hookPhase == HookPhase.Attached;
        internal bool GrappleAttached => hookPhase == HookPhase.Attached;
        internal bool GrappleVisible => hookPhase != HookPhase.Ready;
        internal Vector3 GrappleTip => hookTip;
#if !PE_DISTRIBUTION_BUILD
        internal bool grappleTestPressed;
#endif

        Vector3 GrappleBody => transform.position + cc.center;
        Vector3 GrappleLineOrigin => transform.position + Vector3.up * (cc.height * 0.76f);
        Vector3 GrappleFacing => new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        Vector3 GrappleMount => GrappleVisual.HarnessMount(transform.position, Right(), GrappleFacing, cc.height);

        void ReadGrapple(bool inputEnabled, bool jumpPressed, float dt)
        {
            if (!inputEnabled || Downed || CheatFlight || Mantling || !cc.enabled ||
                (FloorDirector.I != null && FloorDirector.I.transitioning))
            {
                ClearGrapple();
                return;
            }

            bool pressed = SeatInput.GetKeyDown(KeyCode.LeftAlt) || TouchControls.Down(TouchControls.Action.Grapple);
#if !PE_DISTRIBUTION_BUILD
            pressed |= GameManager.autoTest && grappleTestPressed;
            grappleTestPressed = false;
#endif
            if (GrappleLive && (pressed || jumpPressed))
            {
                ReleaseGrapple();
                coyote = jumpBuf = 0f;
            }
            else if (pressed)
            {
                launchGrapple = true;
                return;
            }

            if (hookPhase == HookPhase.Returning)
            {
                Vector3 mount = GrappleMount;
                hookTip = Vector3.MoveTowards(hookTip, mount, ReturnSpeed * dt);
                if ((hookTip - mount).sqrMagnitude < 0.16f) ClearGrapple();
                return;
            }
            if (!GrappleLive) return;

            Vector3 target = hookLocalPoint;
            if (hookSurface != null && hookSurface.enabled && hookSurface.gameObject.activeInHierarchy)
                target = hookSurface.transform.TransformPoint(hookLocalPoint);
            else if (hookPhase == HookPhase.Attached || !ReferenceEquals(hookSurface, null))
            {
                ReleaseGrapple();
                return;
            }

            if (hookPhase == HookPhase.Flying)
            {
                hookTravel += ShotSpeed * dt;
                float length = Vector3.Distance(hookFrom, target);
                hookTip = Vector3.Lerp(hookFrom, target, hookTravel / Mathf.Max(length, 0.01f));
                if (hookTravel < length) return;
                if (hookSurface == null || !GrappleLineClear(GrappleLineOrigin, target))
                {
                    ReleaseGrapple();
                    return;
                }
                ResetParkour();
                hookPhase = HookPhase.Attached;
                coyote = jumpBuf = mantleWindow = 0f;
                slide = false;
                FovPunch(2.2f);
                Sfx.UI(SoundBank.Impact(PryingEyes.Surface.Metal), 0.38f, 1.32f);
                Sfx.At(SoundBank.Impact(PryingEyes.Surface.Metal), target, 0.45f, 1.08f);
            }
            else if (!GrappleLineClear(GrappleLineOrigin, target))
            {
                ReleaseGrapple();
                return;
            }
            hookTip = target;
        }

        void LaunchGrapple()
        {
            launchGrapple = false;
            if (Mantling || !cc.enabled) return;
            Ray aim = new Ray(cam.position, cam.forward);
            hookTravel = hookBlocked = 0f;
            const QueryTriggerInteraction ignore = QueryTriggerInteraction.Ignore;
            bool found = Physics.Raycast(aim, out var hit, GrappleRange, HotelNav.EnvMask, ignore);
            Vector3 target = found ? hit.point : aim.GetPoint(GrappleRange);
            hookFrom = hookTip = GrappleVisual.Muzzle(GrappleMount, GrappleFacing, target);
            Vector3 path = target - hookFrom;
            if (Physics.Raycast(hookFrom, path, out var front, path.magnitude, HotelNav.EnvMask, ignore))
            {
                found = true;
                hit = front;
            }
            if (found)
            {
                hookSurface = hit.collider;
                hookLocalPoint = hookSurface.transform.InverseTransformPoint(hit.point + hit.normal * 0.025f);
            }
            else
            {
                hookSurface = null;
                hookLocalPoint = target;
            }
            StopWingsuit();
            hookPhase = HookPhase.Flying;
            Sfx.UI(SoundBank.KnifeSwish, 0.43f, 1.26f);
        }

        bool GrappleLineClear(Vector3 from, Vector3 to)
        {
            Vector3 line = to - from;
            float length = line.magnitude;
            if (length > GrappleRange + 2f) return false;
            return !Physics.Raycast(from, line, out var obstruction, length, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) ||
                (obstruction.collider == hookSurface && length - obstruction.distance < 0.09f);
        }

        void MoveGrapple(float dt, Vector3 steer)
        {
            Vector3 gap = hookTip - GrappleBody;
            float distance = gap.magnitude;
            Vector3 axis = gap.normalized;
            float closing = Vector3.Dot(vel, axis);
            Vector3 sideways = vel - axis * closing;
            if (distance <= GrappleReel.StopDistance + 0.06f)
            {
                vel = sideways + axis * Mathf.Min(closing, 2f);
                ReleaseGrapple();
                return;
            }

            Vector3 desired = Vector3.ProjectOnPlane(steer * 8f + Vector3.down * (gravity * 0.24f), axis);
            sideways = Vector3.MoveTowards(sideways, desired, 9f * dt);
            closing = GrappleReel.Advance(closing, distance, dt);
            vel = axis * closing + sideways;
            Vector3 before = transform.position;
            CollisionFlags contact = MoveBody(dt);
            if (contact == CollisionFlags.None || dt <= 0f)
            {
                hookBlocked = 0f;
                return;
            }

            Vector3 actual = (transform.position - before) / dt;
            if ((contact & (CollisionFlags.Sides | CollisionFlags.Above)) != 0)
                vel = Vector3.ClampMagnitude(actual, vel.magnitude);
            else if (vel.y < 0f)
                vel.y = Mathf.Max(vel.y, actual.y);
            if (closing > 0.5f && Vector3.Dot(actual, axis) < closing * 0.25f) hookBlocked += dt;
            else hookBlocked = 0f;
            if (hookBlocked >= 0.18f) ReleaseGrapple();
        }

        void ReleaseGrapple()
        {
            hookPhase = HookPhase.Returning;
            hookSurface = null;
        }

        void ClearGrapple()
        {
            hookPhase = HookPhase.Ready;
            hookSurface = null;
            launchGrapple = false;
            hookVisual?.Hide();
        }

        void DrawGrapple()
        {
            if (!GrappleVisible) return;
            hookVisual ??= new GrappleVisual(transform);
            hookVisual.Draw(GrappleMount, GrappleFacing, hookTip, GrappleAttached);
        }

        void OnDisable() => ClearGrapple();
    }
}
