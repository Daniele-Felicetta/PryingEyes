using UnityEngine;

namespace PryingEyes
{
    public static partial class SoundGen
    {
        public static float[] KeeperBell()
        {
            var s = Buf(1.1f);
            Chime(s, 0f, 920f, 0.9f, new[] { 1f, 1.67f, 2.71f }, new[] { 0.5f, 0.20f, 0.08f }, 0.8f);
            Normalize(s, 0.55f);
            return s;
        }
        public static float[] KeeperKeys()
        {
            var s = Buf(3.7f);
            for (int i = 0; i < 6; i++)
            {
                float t = 0.16f + i * 0.53f;
                Chime(s, t, 780f + i % 3 * 113f, 0.25f, new[] { 1f, 1.71f, 2.73f }, new[] { 0.18f, 0.10f, 0.04f }, 0.32f);
                Thump(s, t + 0.04f, 170f, 70f, 0.10f, 0.10f, 0.20f);
            }
            Normalize(s, 0.38f);
            return s;
        }

        public static float[] VigilBreath()
        {
            var s = Buf(4.4f);
            var rng = new System.Random(741);
            var filter = Biquad.BandPass(1250f, 0.7f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float envelope = Mathf.Pow(Mathf.Sin(Mathf.PI * t / 4.4f), 2f);
                s[i] = (filter.Run(N(rng)) * 0.24f + Mathf.Sin(TwoPi * 147f * t) * 0.035f) * envelope;
            }
            Normalize(s, 0.25f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip keeperKeys, keeperBell, vigilBreath;
        public static AudioClip KeeperBell => One(ref keeperBell, "campanelloDelCustode", SoundGen.KeeperBell);
        public static AudioClip KeeperKeys => One(ref keeperKeys, "chiaviDelCustode", SoundGen.KeeperKeys);
        public static AudioClip VigilBreath => One(ref vigilBreath, "respiroDellaVeglia", SoundGen.VigilBreath);
    }
}
