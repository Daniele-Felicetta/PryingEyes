using System;

namespace PryingEyes
{
    public static class GrappleReel
    {
        public const float StopDistance = 1.12f;
        const float TopSpeed = 20f, Pull = 36f, Brake = 58f;

        public static float Advance(float closingSpeed, float distance, float dt)
        {
            // Allow for this frame's travel as well as the remaining braking distance.
            float step = Brake * dt;
            float remaining = Math.Max(0f, distance - StopDistance);
            float target = Math.Min(TopSpeed, MathF.Sqrt(step * step + 2f * Brake * remaining) - step);
            return closingSpeed < target
                ? Math.Min(target, closingSpeed + Pull * dt)
                : Math.Max(target, closingSpeed - step);
        }
    }
}
