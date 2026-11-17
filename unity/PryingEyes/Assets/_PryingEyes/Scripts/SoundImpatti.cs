using System;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Suoni dei proiettili che colpiscono le superfici (2026-11-15), tre varianti per famiglia (Surfaces): intonaco che si
    /// sbriciola, pietra che schiocca, piastrella che tintinna, legno che bussa, metallo che suona, stoffa sorda, vetro che si crepa e
    /// tintinna cadendo. Piu' il fischio del rimbalzo.</summary>
    public static partial class SoundGen
    {
        public static float[] SurfaceHit(Surface surface, int seed)
        {
            var r = new System.Random(seed);
            float[] s;
            switch (surface)
            {
                case Surface.Stone:
                    s = Buf(0.4f);
                    Burst(s, 0f, U(r, 3200f, 4000f), 1.2f, 0.006f, 1.0f, r);                                  // lo schiocco
                    Chime(s, 0f, U(r, 1900f, 2600f), 0.025f, new[] { 1f, 2.3f, 3.9f }, new[] { 1f, 0.5f, 0.3f }, 0.35f);
                    Thump(s, 0f, 240f, 150f, 0.015f, 0.03f, 0.45f);
                    for (int k = 0; k < 5; k++) Burst(s, 0.02f + (float)r.NextDouble() * 0.2f, U(r, 4000f, 8000f), 2.5f, 0.003f, U(r, 0.1f, 0.3f), r);
                    break;
                case Surface.Tile:
                    s = Buf(0.45f);
                    Burst(s, 0f, U(r, 4200f, 5200f), 1.3f, 0.005f, 1.0f, r);
                    Chime(s, 0f, U(r, 2900f, 3700f), 0.04f, new[] { 1f, 1.6f, 2.9f }, new[] { 1f, 0.55f, 0.3f }, 0.5f);
                    Thump(s, 0f, 300f, 180f, 0.012f, 0.025f, 0.35f);
                    for (int k = 0; k < 6; k++) Burst(s, 0.03f + (float)r.NextDouble() * 0.28f, U(r, 5000f, 9000f), 3f, 0.0025f, U(r, 0.12f, 0.32f), r);
                    break;
                case Surface.Wood:
                    s = Buf(0.4f);
                    Thump(s, 0f, U(r, 420f, 560f), U(r, 290f, 370f), 0.02f, 0.05f, 0.9f);                     // l'asse che risuona
                    Burst(s, 0f, U(r, 1100f, 1500f), 1.3f, 0.018f, 0.8f, r);
                    Burst(s, 0f, 4200f, 1.5f, 0.004f, 0.35f, r);
                    for (int k = 0; k < 4; k++) Burst(s, 0.01f + (float)r.NextDouble() * 0.12f, U(r, 2000f, 4500f), 3f, 0.003f, U(r, 0.1f, 0.25f), r);
                    break;
                case Surface.Metal:
                    s = Buf(0.9f);
                    Chime(s, 0f, U(r, 900f, 1700f), 0.22f, new[] { 1f, 2.76f, 5.40f, 8.93f, 1.51f }, new[] { 1f, 0.7f, 0.45f, 0.25f, 0.5f }, 0.7f);
                    Burst(s, 0f, 6000f, 1.5f, 0.004f, 0.8f, r);
                    Thump(s, 0f, 320f, 220f, 0.01f, 0.03f, 0.35f);
                    break;
                case Surface.Fabric:
                {
                    s = Buf(0.25f);
                    var lp = Biquad.LowPass(520f, 0.7f);
                    int n = (int)(0.08f * Rate);
                    for (int i = 0; i < n; i++) { float t = (float)i / Rate; s[i] += lp.Run(N(r)) * Mathf.Exp(-t / 0.018f) * 2.2f; }
                    Thump(s, 0f, 130f, 90f, 0.02f, 0.04f, 0.5f);
                    break;
                }
                case Surface.Glass:
                    s = Buf(0.8f);
                    Burst(s, 0f, 5200f, 1.0f, 0.008f, 1.0f, r);                                                // la crepa
                    for (int k = 0; k < 9; k++)                                                                 // le schegge che cadono
                        Chime(s, (float)r.NextDouble() * 0.35f, U(r, 2800f, 7200f), U(r, 0.03f, 0.09f), new[] { 1f, 2.32f, 4.25f }, new[] { 1f, 0.4f, 0.2f }, U(r, 0.15f, 0.4f));
                    break;
                default:                                                                                        // intonaco
                    s = Buf(0.45f);
                    Burst(s, 0f, U(r, 1500f, 2200f), 0.8f, 0.03f, 0.9f, r);
                    Thump(s, 0f, 190f, 120f, 0.02f, 0.045f, 0.6f);
                    Burst(s, 0f, 5200f, 1.0f, 0.006f, 0.5f, r);
                    for (int k = 0; k < 7; k++) Burst(s, 0.03f + (float)r.NextDouble() * 0.3f, U(r, 2500f, 6000f), 2f, 0.004f, U(r, 0.08f, 0.25f), r);   // le briciole
                    break;
            }
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Il fischio di un proiettile che rimbalza: un seno che scende da 3-4 kHz a 1-1,5 kHz in mezzo secondo, con un tremolio
        /// veloce, dopo il colpo secco.</summary>
        public static float[] Ricochet(int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(0.5f);
            double ph = 0;
            float fA = U(r, 3200f, 4200f), fB = U(r, 1000f, 1500f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, k = t / 0.45f;
                float f = Mathf.Lerp(fA, fB, Mathf.Pow(Mathf.Clamp01(k), 0.6f)) * (1f + 0.02f * Mathf.Sin(TwoPi * 37f * t));
                ph += TwoPi * f / Rate;
                float env = Mathf.Clamp01(t / 0.01f) * Mathf.Exp(-t / 0.16f);
                s[i] = (float)Math.Sin(ph) * env * 0.6f + (float)Math.Sin(2 * ph) * env * 0.15f;
            }
            Burst(s, 0f, 5000f, 1.2f, 0.004f, 0.6f, r);
            Normalize(s, 0.8f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static readonly AudioClip[][] surfaceHits = new AudioClip[7][];
        static AudioClip[] ricochets;

        /// <summary>Uno dei tre colpi della superficie.</summary>
        public static AudioClip Impact(Surface s)
        {
            int i = (int)s;
            if (surfaceHits[i] == null)
            {
                if (prefetching) { PrefetchSet("impatto_" + s, 3, k => SoundGen.SurfaceHit(s, 500 + i * 10 + k)); return null; }
                surfaceHits[i] = MakeSet("impatto_" + s, 3, k => SoundGen.SurfaceHit(s, 500 + i * 10 + k));
            }
            return surfaceHits[i][UnityEngine.Random.Range(0, 3)];
        }

        public static AudioClip Ricochet => Pick(ref ricochets, 2, "rimbalzo", i => SoundGen.Ricochet(600 + i));

        /// <summary>Prepara i colpi di tutte le superfici (pochi millisecondi), cosi' il primo sparo su un muro non fa scattare il gioco.</summary>
        public static void WarmImpacts()
        {
            for (int i = 0; i < surfaceHits.Length; i++) _ = Impact((Surface)i);
            _ = Ricochet;
        }
    }
}
