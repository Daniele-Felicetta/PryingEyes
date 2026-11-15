using UnityEngine;

namespace PryingEyes
{
    /// <summary>I suoni dei lampadari che crollano (Proc/Chandeliers.cs), procedurali come gli altri: la catena che si spezza (uno
    /// schiocco di ferro e gli anelli che sbattono) e lo schianto (tonfo pesante, ferro che risuona, una pioggia di cristalli che si
    /// dirada, detriti).</summary>
    public static partial class SoundGen
    {
        /// <summary>0,7 s.</summary>
        public static float[] ChainSnap()
        {
            var r = new System.Random(53);
            var s = Buf(0.7f);
            Burst(s, 0f, 2600f, 1.8f, 0.012f, 1.0f, r);                       // lo schiocco
            Chime(s, 0f, 1720f, 0.09f, new[] { 1f, 2.76f, 5.4f }, new[] { 0.5f, 0.3f, 0.15f }, 0.6f);
            for (int k = 0; k < 9; k++)                                        // gli anelli che sbattono, sempre piu' radi
            {
                float at = 0.03f + k * k * 0.006f + U(r, 0f, 0.01f);
                Burst(s, at, U(r, 2000f, 4200f), 2.2f, 0.004f, 0.45f * (1f - k / 10f), r);
            }
            var hp = Biquad.HighPass(180f, 0.7f);
            for (int i = 0; i < s.Length; i++) s[i] = hp.Run(s[i]);
            Normalize(s, 0.85f);
            return s;
        }

        /// <summary>2,4 s.</summary>
        public static float[] ChandelierCrash()
        {
            var r = new System.Random(59);
            var s = Buf(2.4f);
            Thump(s, 0f, 72f, 34f, 0.18f, 0.22f, 1.3f);                        // il peso che tocca terra
            Burst(s, 0f, 420f, 0.5f, 0.09f, 1.2f, r);                          // schianto
            Burst(s, 0.004f, 1500f, 0.8f, 0.05f, 0.9f, r);
            // ferro che risuona (parziali non armoniche)
            Chime(s, 0.01f, 310f, 0.5f, new[] { 1f, 2.32f, 4.25f, 6.63f }, new[] { 0.6f, 0.4f, 0.25f, 0.12f }, 0.55f);
            Chime(s, 0.06f, 455f, 0.35f, new[] { 1f, 2.51f, 4.7f }, new[] { 0.5f, 0.3f, 0.15f }, 0.35f);
            // cristalli: tintinnii acuti fitti all'inizio, poi radi
            for (int k = 0; k < 70; k++)
            {
                float at = 0.02f + Mathf.Pow((float)r.NextDouble(), 1.8f) * 1.6f;
                float f = U(r, 2300f, 6200f);
                float g = 0.22f * (1f - at / 1.9f);
                Chime(s, at, f, U(r, 0.04f, 0.14f), new[] { 1f, 2.1f, 3.9f }, new[] { 0.6f, 0.25f, 0.1f }, g);
            }
            // detriti che rotolano
            for (int k = 0; k < 18; k++)
            {
                float at = 0.08f + U(r, 0f, 0.9f);
                Burst(s, at, U(r, 600f, 2000f), 1.4f, 0.015f, 0.3f * (1f - at / 1.2f), r);
            }
            var lp = Biquad.LowPass(9000f);
            for (int i = 0; i < s.Length; i++) s[i] = lp.Run(s[i]);
            Normalize(s, 0.95f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip chainSnap, chandelierCrash;
        public static AudioClip ChainSnap => One(ref chainSnap, "catena", SoundGen.ChainSnap);
        public static AudioClip ChandelierCrash => One(ref chandelierCrash, "lampadario", SoundGen.ChandelierCrash);
    }
}
