using PryingEyes.Deserto;
using UnityEngine;

namespace PryingEyes
{
    public partial class PlayerController
    {
        internal const float SwimSpeed = 2.65f;
        const float FloatDepth = 1.32f;
        public bool Swimming { get; private set; }
        internal float SwimPhase { get; private set; }
        internal float SwimEffort { get; private set; }
        internal float SwimYaw { get; private set; }
        internal float SwimTurn { get; private set; }
        internal float SwimSurface { get; private set; }
        internal float SwimSubmersion { get; private set; }
        internal float SwimDivePitch { get; private set; }
        float swimHandsBlend, entrySink, swimDepthTarget, bubbleTimer;
        AudioLowPassFilter swimAudio;
        bool swimHint;
#if !PE_DISTRIBUTION_BUILD
        internal float swimTestVertical;
#endif

        bool UpdateWater(out float surface, out float depth)
        {
            var p = transform.position;
            depth = 0f;
            DesertLake lake = default;
            bool wet = !CheatFlight && DesertMode.OnSurface &&
                DesertWorld.LakeAt(p.x, p.z, out lake, out depth) &&
                p.y < lake.Surface - 0.06f && p.y >= lake.Surface - depth - 0.65f;
            surface = lake.Surface;
            bool swim = wet && depth >= (Swimming ? 1.02f : 1.42f) &&
                (Swimming || p.y <= surface - 0.96f);
            if (swim == Swimming) return wet;

            Swimming = swim;
            if (swim)
            {
                float entry = Mathf.InverseLerp(1.8f, 9f, -vel.y);
                CancelMantle();
                weapon.BeginParkour();
                slide = crouch = false;
                crouchToggled = false;
                jumpBuf = coyote = 0f;
                vel = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(vel, Vector3.up), SwimSpeed) - Vector3.up * (entry * 1.1f);
                SwimYaw = yaw;
                SwimPhase = 0.88f;
                SwimSurface = surface;
                swimDepthTarget = FloatDepth;
                entrySink = 0.17f * entry;
                DesertLakeSurface.Contact(SurfacePoint(transform.position, surface), Vector3.zero, 0.42f + entry * 0.9f);
                if (!swimHint && !GameManager.autoTest)
                {
                    swimHint = true;
                    hud?.Toast(PadControls.Preferred ? "Tieni B per immergerti e A per risalire." :
                        "Immergiti con C o Ctrl. Tieni Spazio per risalire.");
                }
            }
            else
            {
                vel.y = Mathf.Min(0f, vel.y);
                SwimSubmersion = 0f;
            }
            return wet;
        }

        void SwimMove(float surface, float lakeDepth, Vector3 wish, float wishAmount, float dive, float dt)
        {
            float speed = Downed ? 0.85f : SwimSpeed;
            float effort = wishAmount * (Downed ? 0.45f : 1f);
            SwimEffort = Mathf.Lerp(SwimEffort, effort, 1f - Mathf.Exp(-(effort > SwimEffort ? 5.4f : 4.3f) * dt));
            SwimSurface = surface;
            float previousPhase = SwimPhase;
            SwimPhase = Mathf.Repeat(SwimPhase + SwimStroke.Rate(SwimEffort) * dt, 1f);

            Vector3 heading = Vector3.ProjectOnPlane(vel, Vector3.up);
            float targetYaw = heading.sqrMagnitude > 0.12f ? Mathf.Atan2(heading.x, heading.z) : yaw;
            float error = Mathf.DeltaAngle(SwimYaw * Mathf.Rad2Deg, targetYaw * Mathf.Rad2Deg);
            SwimYaw += error * Mathf.Deg2Rad * (1f - Mathf.Exp(-8f * dt));
            SwimTurn = Mathf.Lerp(SwimTurn, Mathf.Clamp(error / 55f, -1f, 1f), 1f - Mathf.Exp(-6f * dt));

            // Damped buoyancy keeps the eye above the water while the capsule can still meet the sloping bed.
            float stroke = Mathf.Cos(4f * Mathf.PI * (SwimPhase - 0.28f));
            entrySink *= Mathf.Exp(-4.8f * dt);
            swimDepthTarget = Mathf.Clamp(swimDepthTarget + dive * 2.55f * dt, FloatDepth, Mathf.Max(FloatDepth, lakeDepth - 0.16f));
            float submersion = Mathf.InverseLerp(FloatDepth + 0.10f, FloatDepth + 0.88f, surface - transform.position.y);
            SwimSubmersion = Mathf.Lerp(SwimSubmersion, submersion, 1f - Mathf.Exp(-10f * dt));
            float bob = Mathf.Sin(4f * Mathf.PI * SwimPhase) * (0.014f + 0.019f * SwimEffort) * (1f - SwimSubmersion * 0.7f);
            float lift = Mathf.Clamp((surface - swimDepthTarget + bob - entrySink - transform.position.y) * 6f, -2.65f, 3.3f);
            Vector3 target = wish * (speed * wishAmount * (1f + stroke * Mathf.Lerp(0.16f, 0.09f, SwimSubmersion) * SwimEffort));
            heading = Vector3.Lerp(heading, target, 1f - Mathf.Exp(-(wishAmount > 0.05f ? 5.8f : 5.2f) * dt));
            vel = heading + Vector3.up * Mathf.Lerp(vel.y, lift, 1f - Mathf.Exp(-9f * dt));
            MoveBody(dt);
            if (cc.isGrounded && dive <= 0f && SwimSubmersion > 0.45f)
                swimDepthTarget = Mathf.Min(swimDepthTarget, surface - transform.position.y + 0.08f);
            float grade = Mathf.Atan2(-vel.y, Mathf.Max(0.65f, heading.magnitude)) * Mathf.Rad2Deg;
            SwimDivePitch = Mathf.Lerp(SwimDivePitch, Mathf.Clamp(grade, -45f, 59f) * SwimSubmersion, 1f - Mathf.Exp(-7f * dt));

            if (SwimSubmersion < 0.36f)
                DesertLakeSurface.StrokeContacts(transform.position, SwimYaw, surface, SwimEffort, previousPhase, SwimPhase);
            if (SwimSubmersion < 0.82f) bubbleTimer = 0f;
            else
            {
                bubbleTimer -= dt;
                if (bubbleTimer <= 0f)
                {
                    Vector3 forward = new Vector3(Mathf.Sin(SwimYaw), 0f, Mathf.Cos(SwimYaw));
                    DesertLakeSurface.Bubbles(transform.position + Vector3.up * 0.92f + forward * 0.35f);
                    bubbleTimer = Mathf.Lerp(1.7f, 0.95f, SwimEffort);
                }
            }
        }

        static Vector3 SurfacePoint(Vector3 p, float surface) => new Vector3(p.x, surface + 0.016f, p.z);

        void UpdateSwimAudio(float eyeY)
        {
            // Split screen has one listener and must not apply the second seat's filter to it.
            bool underwater = Swimming && eyeY < SwimSurface - 0.065f && cam != null && cam.TryGetComponent<AudioListener>(out var listener) && listener.enabled;
            if (underwater && swimAudio == null) swimAudio = cam.gameObject.AddComponent<AudioLowPassFilter>();
            if (swimAudio == null) return;
            swimAudio.enabled = underwater;
            if (underwater) swimAudio.cutoffFrequency = Mathf.Lerp(2650f, 840f, Mathf.InverseLerp(0.05f, 1.2f, SwimSurface - eyeY));
        }

        void ResetSwimming()
        {
            if (!Swimming && swimHandsBlend <= 0f) return;
            Swimming = false;
            swimHandsBlend = SwimEffort = SwimTurn = entrySink = 0f;
            SwimSubmersion = SwimDivePitch = 0f;
            if (swimAudio != null) swimAudio.enabled = false;
            climbHands?.Hide();
        }
    }

}
