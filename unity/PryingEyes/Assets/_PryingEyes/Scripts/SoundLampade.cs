using UnityEngine;

namespace PryingEyes
{
    /// <summary>Il suono della lampada che si rompe (Proc/BreakableLamps.cs): uno schiocco secco di vetro sottile, un ronzio elettrico
    /// che si spegne di colpo e qualche scheggia che tintinna.</summary>
    public static partial class SoundGen
    {
        /// <summary>0,7 s.</summary>
        public static float[] BulbPop()
        {
            var r = new System.Random(61);
            var s = Buf(0.7f);
            Thump(s, 0f, 1100f, 520f, 0.01f, 0.012f, 0.8f);                    // lo schiocco del bulbo
            Burst(s, 0f, 3600f, 1.6f, 0.01f, 1.0f, r);
            Burst(s, 0.002f, 7000f, 2.0f, 0.004f, 0.5f, r);                    // l'arco del filamento
            for (int k = 0; k < 14; k++)                                       // schegge
            {
                float at = 0.02f + (float)r.NextDouble() * 0.45f;
                Chime(s, at, U(r, 3000f, 7500f), U(r, 0.02f, 0.06f), new[] { 1f, 2.3f }, new[] { 0.6f, 0.2f }, 0.25f * (1f - at / 0.6f));
            }
            var hp = Biquad.HighPass(260f, 0.7f);
            for (int i = 0; i < s.Length; i++) s[i] = hp.Run(s[i]);
            Normalize(s, 0.85f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip bulbPop;
        public static AudioClip BulbPop => One(ref bulbPop, "lampadina", SoundGen.BulbPop);
    }
}
