using UnityEngine;

namespace PryingEyes
{
    /// <summary>Il rintocco della medaglia (Medals.cs): due note di campanello in quinta, la seconda un filo dopo, con un luccichio.</summary>
    public static partial class SoundGen
    {
        /// <summary>1,1 s.</summary>
        public static float[] Medal()
        {
            var r = new System.Random(67);
            var s = Buf(1.1f);
            var ratios = new[] { 1f, 2.0f, 3.01f, 4.2f };
            var amps = new[] { 0.7f, 0.25f, 0.12f, 0.06f };
            Chime(s, 0f, 1046.5f, 0.45f, ratios, amps, 0.7f);
            Chime(s, 0.085f, 1568f, 0.55f, ratios, amps, 0.6f);
            for (int k = 0; k < 10; k++)
            {
                float at = 0.05f + (float)r.NextDouble() * 0.35f;
                Chime(s, at, U(r, 4200f, 7800f), 0.05f, new[] { 1f }, new[] { 1f }, 0.05f);
            }
            Normalize(s, 0.8f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip medal;
        public static AudioClip Medal => One(ref medal, "medaglia", SoundGen.Medal);
    }
}
