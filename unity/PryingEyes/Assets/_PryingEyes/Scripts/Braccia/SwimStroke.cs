using UnityEngine;

namespace PryingEyes
{
    static class SwimStroke
    {
        internal const float Catch = 0.035f;

        // Metres out from the shoulder, up from the water, and forward from the shoulder.
        // The wrist tips into the catch, sweeps under the torso, then feathers on an elbow-led recovery.
        static readonly Vector3[] wrists =
        {
            new Vector3(0.04f, 0.07f, 0.51f),
            new Vector3(0.07f, -0.19f, 0.43f),
            new Vector3(0.17f, -0.30f, 0.19f),
            new Vector3(0.15f, -0.29f, -0.16f),
            new Vector3(0.16f, -0.10f, -0.33f),
            new Vector3(0.28f, 0.10f, -0.23f),
            new Vector3(0.30f, 0.24f, 0.03f),
            new Vector3(0.15f, 0.21f, 0.36f)
        };

        // Palm pitch, outward yaw and roll in degrees. The final component feathers the palm as it leaves the water.
        static readonly Vector3[] palms =
        {
            new Vector3(27f, -5f, 6f),
            new Vector3(60f, -7f, 12f),
            new Vector3(75f, 12f, 19f),
            new Vector3(64f, 22f, 11f),
            new Vector3(32f, 30f, 35f),
            new Vector3(9f, 27f, 62f),
            new Vector3(-9f, 19f, 52f),
            new Vector3(4f, 4f, 28f)
        };

        internal static float Rate(float effort) => Mathf.Lerp(0.46f, 0.88f, effort);
        internal static float ArmPhase(float phase, int arm) => Mathf.Repeat(phase + arm * 0.5f, 1f);
        internal static float Recovery(float phase) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.70f, phase))
            * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.87f, 1f, phase)));

        internal static Vector3 Wrist(float phase) => Curve(wrists, phase);
        internal static Vector3 Palm(float phase) => Curve(palms, phase);

        internal static bool Crossed(float before, float after, int arm, float key)
        {
            float from = ArmPhase(before, arm);
            return Mathf.Repeat(key - from, 1f) < Mathf.Repeat(after - before, 1f);
        }

        static Vector3 Curve(Vector3[] keys, float phase)
        {
            float sample = phase * keys.Length;
            int i = Mathf.FloorToInt(sample);
            float t = sample - i, t2 = t * t, t3 = t2 * t;
            int n = keys.Length;
            Vector3 a = keys[(i + n - 1) % n], b = keys[i % n], c = keys[(i + 1) % n], d = keys[(i + 2) % n];
            return 0.5f * ((2f * b) + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t2 +
                (3f * b - a - 3f * c + d) * t3);
        }
    }
}
