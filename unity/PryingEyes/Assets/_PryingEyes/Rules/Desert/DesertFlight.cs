using System;

namespace PryingEyes.Deserto
{
    /// <summary>Arcade flight speeds in metres per second, and the ceiling above the sand.</summary>
    public static class DesertFlight
    {
        public const float TakeoffSpeed = 18f, TopSpeed = 38f, ClimbSpeed = 7f, Ceiling = 110f;
        const float StallSpeed = 14f;

        public static float ForwardSpeed(float speed, float throttle, bool grounded, float dt)
        {
            speed = Math.Max(0f, speed);
            if (throttle > 0.05f)
                return Math.Min(TopSpeed, speed + 9f * throttle * (1f - speed / TopSpeed) * dt);
            float drag = throttle < -0.05f ? -throttle * (grounded ? 14f : 4f) : grounded ? 2.2f : 0.65f;
            return Math.Max(0f, speed - drag * dt);
        }

        public static float VerticalSpeed(float speed, bool grounded, bool climb, bool descend, float headroom)
        {
            if (grounded && (!climb || descend || speed < TakeoffSpeed || headroom <= 0f)) return -4f;
            if (descend || speed < StallSpeed) return -5f;
            if (climb && speed >= TakeoffSpeed) return Math.Clamp(headroom * 2f, 0f, ClimbSpeed);
            return 0f;
        }
    }
}
