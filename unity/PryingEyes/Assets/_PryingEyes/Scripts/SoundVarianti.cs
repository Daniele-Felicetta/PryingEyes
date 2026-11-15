using System;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Suoni delle varianti dell'orda, procedurali come tutti gli altri: lo scoppio dell'Appestato, la pietra che gratta
    /// della Statua, il ruggito del Cardinale.</summary>
    public static partial class SoundGen
    {
        /// <summary>Scoppio umido: tonfo basso, schianto di rumore e un gorgoglio di bolle sempre piu' rade. 1,4 s.</summary>
        public static float[] PlagueBurst()
        {
            var r = new System.Random(41);
            var s = Buf(1.4f);
            Thump(s, 0f, 95f, 38f, 0.18f, 0.16f, 1.2f);
            Burst(s, 0f, 700f, 0.6f, 0.05f, 1.4f, r);
            Burst(s, 0.005f, 240f, 0.9f, 0.12f, 1.1f, r);
            Burst(s, 0.02f, 1800f, 1.2f, 0.03f, 0.5f, r);
            for (int b = 0; b < 26; b++)
            {
                float at = 0.06f + (float)Math.Pow(U(r, 0f, 1f), 1.6f) * 1.1f;
                float f0 = U(r, 180f, 420f);
                Thump(s, at, f0, f0 * U(r, 1.6f, 2.4f), 0.03f, 0.018f, 0.35f * (1f - at / 1.3f));
            }
            var lp = Biquad.LowPass(5200f);
            for (int i = 0; i < s.Length; i++) s[i] = lp.Run(s[i]);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Pietra che striscia sulla pietra: rumore scuro e granuloso che sale e scende, con scricchiolii rari. Loop di ~3,5 s.</summary>
        public static float[] StoneGrind()
        {
            var r = new System.Random(43);
            var s = Buf(4f);
            var lp = Biquad.LowPass(650f, 0.9f);
            var lp2 = Biquad.LowPass(650f, 0.9f);
            var crack = Biquad.BandPass(2600f, 1.5f);
            float brown = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                brown = Mathf.Clamp(brown + N(r) * 0.06f, -1f, 1f) * 0.995f;
                float rough = 0.6f + 0.4f * Mathf.Sin(TwoPi * 1.7f * t + Mathf.Sin(TwoPi * 0.37f * t) * 2f);
                float x = lp2.Run(lp.Run(brown * 3f)) * rough;
                x += crack.Run(r.NextDouble() < 0.0009 ? N(r) * 6f : 0f);
                s[i] = x;
            }
            s = LoopSeamless(s, Rate / 2);
            Normalize(s, 0.75f);
            return s;
        }

        /// <summary>Ruggito del capo: tre voci basse e scordate dentro due formanti, un respiro di rumore, la voce che cala. 2,2 s.</summary>
        public static float[] BossRoar()
        {
            var r = new System.Random(47);
            var s = Buf(2.2f);
            var tab = SawTable();
            var ph = new double[3];
            float[] det = { 1f, 1.013f, 0.987f };
            var f1 = Biquad.BandPass(520f, 1.6f);
            var f2 = Biquad.BandPass(1150f, 2.2f);
            var lp = Biquad.LowPass(2400f);
            var breath = Biquad.BandPass(900f, 0.8f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Clamp01(t / 0.18f) * Mathf.Clamp01((2.2f - t) / 0.7f);
                float f0 = Mathf.Lerp(92f, 58f, t / 2.2f) * (1f + 0.04f * Mathf.Sin(TwoPi * 5.5f * t));
                float x = 0f;
                for (int v = 0; v < 3; v++)
                {
                    ph[v] += f0 * det[v] / Rate;
                    ph[v] -= Math.Floor(ph[v]);
                    x += tab[(int)(ph[v] * 2048.0) & 2047];
                }
                float voice = f1.Run(x) * 1.2f + f2.Run(x) * 0.6f;
                s[i] = lp.Run((voice + breath.Run(N(r)) * 0.5f) * env);
            }
            Normalize(s, 0.95f);
            return s;
        }
    }

    public static partial class SoundGen
    {
        /// <summary>Esplosione dell'acquasanta e del carillon: tonfo profondo, schianto di rumore che si chiude, crepitio. 1,8 s.</summary>
        public static float[] Blast()
        {
            var r = new System.Random(53);
            var s = Buf(1.8f);
            Thump(s, 0f, 70f, 28f, 0.25f, 0.30f, 1.4f);
            Burst(s, 0f, 420f, 0.5f, 0.22f, 1.6f, r);
            Burst(s, 0.004f, 1500f, 0.8f, 0.06f, 0.9f, r);
            for (int k = 0; k < 40; k++)
            {
                float at = 0.05f + (float)Math.Pow(U(r, 0f, 1f), 2f) * 1.3f;
                Burst(s, at, U(r, 1800f, 4200f), 2f, 0.008f, 0.25f * (1f - at / 1.5f), r);
            }
            var lp = Biquad.LowPass(6000f);
            for (int i = 0; i < s.Length; i++) s[i] = lp.Run(s[i]);
            Normalize(s, 0.95f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip blast;
        public static AudioClip Blast => One(ref blast, "esplosione", SoundGen.Blast);
        static AudioClip plagueBurst, stoneGrind, bossRoar;
        public static AudioClip Burst => One(ref plagueBurst, "scoppio", SoundGen.PlagueBurst);
        public static AudioClip StoneGrind => One(ref stoneGrind, "pietra", SoundGen.StoneGrind);
        public static AudioClip BossRoar => One(ref bossRoar, "ruggito", SoundGen.BossRoar);
    }
}
