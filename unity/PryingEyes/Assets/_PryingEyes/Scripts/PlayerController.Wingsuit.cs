using PryingEyes.Deserto;
using UnityEngine;

namespace PryingEyes
{
    public partial class PlayerController
    {
        Vector3 wingHeading;
        float wingHold, wingBank;
        bool wingBlocked;
        public bool Gliding { get; private set; }
        float WingAmount { get; set; }
        float WingsuitRoll => wingBank * GameSettings.Motion;
#if !PE_DISTRIBUTION_BUILD
        internal bool parkourTestGlide;
#endif

        bool CanOpenWingsuit() => DesertMode.Active && alive && !Downed && !OnClimbWall && !Mantling &&
            !cc.isGrounded && vel.y < -2.8f && !Physics.Raycast(transform.position + Vector3.up * 0.2f,
                Vector3.down, 3.8f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);

        void UpdateWingsuit(float dt, bool held, bool crouchHeld, Vector3 facing)
        {
            if (!held || cc.isGrounded) wingBlocked = false;
            if (!held || crouchHeld || cc.isGrounded || OnClimbWall || Mantling || Downed || !DesertMode.Active)
                StopWingsuit();
            else if (!Gliding && !wingBlocked)
            {
                wingHold = CanOpenWingsuit() && jumpBuf <= 0f ? wingHold + dt : 0f;
                if (wingHold >= 0.14f)
                {
                    wingHeading = facing;
                    Gliding = true;
                    weapon.BeginParkour();
                    FovPunch(3f);
                    Sfx.UI(SoundBank.Impact(PryingEyes.Surface.Fabric), 0.33f, 0.85f);
                }
            }

            WingAmount = Mathf.MoveTowards(WingAmount, Gliding ? 1f : 0f, dt / (Gliding ? 0.24f : 0.20f));
            if (!Gliding)
            {
                wingBank = Mathf.MoveTowards(wingBank, 0f, 40f * dt);
                return;
            }
            float headingError = Vector3.SignedAngle(wingHeading, facing, Vector3.up);
            wingHeading = Vector3.RotateTowards(wingHeading, facing, 105f * Mathf.Deg2Rad * dt, 0f);
            wingBank = Mathf.Lerp(wingBank, Mathf.Clamp(-headingError * 0.45f, -15f, 15f), 1f - Mathf.Exp(-6f * dt));
            float dive = Mathf.InverseLerp(-0.30f, 0.80f, pitch);
            float speed = Mathf.Lerp(8.1f, 16.5f, dive);
            float descent = Mathf.Lerp(2.8f, 9.2f, dive);
            var horizontal = Vector3.MoveTowards(Vector3.ProjectOnPlane(vel, Vector3.up), wingHeading * speed, 18f * dt);
            // The walking update has already integrated gravity for this frame.
            float vertical = Mathf.Lerp(vel.y + gravity * dt, -descent, 1f - Mathf.Exp(-7f * dt));
            vel = horizontal + Vector3.up * vertical;
        }

        void WingsuitContact(CollisionFlags hit)
        {
            if (!Gliding || (hit & (CollisionFlags.Below | CollisionFlags.Sides)) == 0) return;
            Vector3 horizontal = Vector3.ProjectOnPlane(vel, Vector3.up);
            vel = Vector3.ClampMagnitude(horizontal, walk) + Vector3.down * 0.6f;
            wingBlocked = true;
            StopWingsuit();
            if ((hit & CollisionFlags.Below) != 0) Synth.Step(Surface(), 0.54f);
        }

        void StopWingsuit()
        {
            Gliding = false;
            wingHold = 0f;
        }

        void ResetWingsuit()
        {
            StopWingsuit();
            WingAmount = wingBank = 0f;
            wingBlocked = false;
        }
    }
}
