using System;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Il suono della corrente che torna (Proc/Power.cs), procedurale come gli altri: il colpo della leva a coltello, tre
    /// relè che scattano in fila e il ronzio della rete (50 Hz e armoniche) che sale, tiene e si spegne nel resto dell'albergo.</summary>
    public static partial class SoundGen
    {
        /// <summary>2,8 s.</summary>
        public static float[] PowerOn()
        {
            var r = new System.Random(47);
            var s = Buf(2.8f);
            Thump(s, 0f, 78f, 42f, 0.12f, 0.14f, 1.1f);           // il colpo della leva
            Burst(s, 0f, 1300f, 1.4f, 0.025f, 0.9f, r);           // metallo
            Burst(s, 0.004f, 3800f, 2.0f, 0.008f, 0.6f, r);       // l'arco ai contatti
            foreach (float at in new[] { 0.18f, 0.34f, 0.47f, 0.86f })
            {
                Burst(s, at, 2400f, 2.5f, 0.006f, 0.55f, r);      // relè
                Thump(s, at, 160f, 120f, 0.02f, 0.03f, 0.25f);
            }
            // ronzio: 50 Hz e armoniche dispari piu' forti (trasformatore), sale da 0,25 s, tiene, si spegne
            double ph = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Clamp01((t - 0.25f) / 0.9f) * Mathf.Clamp01((2.75f - t) / 1.1f);
                if (env <= 0f) continue;
                ph += TwoPi * 50.0 / Rate;
                float v = 0.30f * (float)Math.Sin(ph * 2) + 0.22f * (float)Math.Sin(ph * 3) + 0.12f * (float)Math.Sin(ph * 4)
                        + 0.10f * (float)Math.Sin(ph * 5) + 0.05f * (float)Math.Sin(ph * 7);
                s[i] += v * env * 0.55f * (1f + 0.08f * (float)Math.Sin(t * 9.0));
            }
            var lp = Biquad.LowPass(7000f);
            for (int i = 0; i < s.Length; i++) s[i] = lp.Run(s[i]);
            Normalize(s, 0.92f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip powerOn;
        public static AudioClip PowerOn => One(ref powerOn, "corrente", SoundGen.PowerOn);
    }
}
