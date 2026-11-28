using System;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>I suoni che il deserto aggiunge al vento di ProcAmbience (DesertSound li suona): il sibilo della sabbia portata dal vento, il coro dei grilli, il gracchio di un avvoltoio,
    /// un ululato lontano e il crepitio di un fuoco. Procedurali come gli altri: nessun file audio, solo rumore filtrato e inviluppi. I loop (sabbia, grilli, fuoco) si chiudono su se stessi.</summary>
    public static partial class SoundGen
    {
        /// <summary>Sabbia sul vento: un sibilo acuto a raffiche lente, quello che si sente in una tempesta. Loop di 10 s.</summary>
        public static float[] SandHiss(int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(11f);
            var hp = Biquad.HighPass(1500f, 0.6f);
            var lp = Biquad.LowPass(7800f, 0.6f);
            var bp = new Biquad();
            float p1 = U(r, 0f, 6f), p2 = U(r, 0f, 6f), p3 = U(r, 0f, 6f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, n = N(r);
                float gust = 0.55f + 0.28f * Mathf.Sin(TwoPi * t / 4.1f + p1) + 0.17f * Mathf.Sin(TwoPi * t / 2.3f + p2);
                if ((i & 63) == 0) bp.SetBandPass(3000f + 1300f * Mathf.Sin(TwoPi * t / 5.7f + p3) * gust, 0.8f, Rate);
                s[i] = (lp.Run(hp.Run(n)) * 0.5f + bp.Run(n) * 1.1f) * gust * gust;
            }
            s = LoopSeamless(s, Rate);
            Normalize(s, 0.6f);
            return s;
        }

        /// <summary>Grilli nella notte: dieci voci, ognuna col suo tono (4,1-5,3 kHz) e il suo ritmo, a trilli di impulsi rapidi. Loop di 8 s: il periodo di ogni voce divide il loop,
        /// quindi i trilli ricominciano uguali e non c'e' giunzione.</summary>
        public static float[] Crickets(int seed)
        {
            var r = new System.Random(seed);
            const float dur = 8f;
            var s = Buf(dur);
            int n = s.Length;
            for (int v = 0; v < 10; v++)
            {
                int count = r.Next(8, 14);
                float period = dur / count, f = U(r, 4100f, 5300f), pulse = U(r, 26f, 38f), len = U(r, 0.13f, 0.23f), start = U(r, 0f, period), gain = U(r, 0.35f, 1f);
                int chirp = (int)(len * Rate);
                for (int k = 0; k < count; k++)
                {
                    int i0 = (int)((start + k * period) * Rate);
                    float fj = f * U(r, 0.985f, 1.015f);
                    double ph = U(r, 0f, TwoPi);
                    for (int i = 0; i < chirp; i++)
                    {
                        float t = (float)i / Rate, u = (float)i / chirp;
                        float c = 0.5f + 0.5f * Mathf.Cos(TwoPi * pulse * t);
                        float env = c * c * Mathf.Sin(Mathf.PI * u);
                        ph += TwoPi * fj / Rate;
                        s[(i0 + i) % n] += (float)Math.Sin(ph) * env * gain;
                    }
                }
            }
            Normalize(s, 0.55f);
            return s;
        }

        /// <summary>Il gracchio di un avvoltoio: due o tre "gra" rauchi, una sega che scende di tono passata per due formanti. Poco meno di un secondo.</summary>
        public static float[] Caw(int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(1.15f);
            var f1 = Biquad.BandPass(U(r, 850f, 1150f), 3f);
            var f2 = Biquad.BandPass(U(r, 2000f, 2800f), 4f);
            var lp = Biquad.LowPass(6000f);
            int caws = r.Next(2, 4);
            double ph = 0;
            float at = 0.02f;
            for (int k = 0; k < caws; k++)
            {
                float len = U(r, 0.17f, 0.24f), f0 = U(r, 440f, 600f);
                int i0 = (int)(at * Rate), n = (int)(len * Rate);
                for (int i = 0; i < n && i0 + i < s.Length; i++)
                {
                    float t = (float)i / Rate, u = (float)i / n;
                    ph += TwoPi * f0 * (1.1f - 0.35f * u) / Rate;
                    float saw = 2f * (float)(ph / TwoPi - Math.Floor(ph / TwoPi)) - 1f;
                    float x = saw * 0.8f + N(r) * 0.35f;
                    float env = Mathf.Clamp01(t / 0.012f) * (1f - Mathf.SmoothStep(0f, 1f, (u - 0.55f) / 0.45f));
                    s[i0 + i] += (f1.Run(x) + f2.Run(x) * 0.7f + lp.Run(x) * 0.15f) * env;
                }
                at += len + U(r, 0.07f, 0.13f);
            }
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Un ululato lontano: una nota che sale, tiene con un po' di vibrato e ricade, con le sue armoniche e un filo di fiato. Tre o quattro secondi.</summary>
        public static float[] Howl(int seed)
        {
            var r = new System.Random(seed);
            float dur = U(r, 3.1f, 3.9f);
            var s = Buf(dur);
            var lp = Biquad.LowPass(2400f, 0.7f);
            var breath = Biquad.BandPass(1100f, 1.2f);
            float f0 = U(r, 330f, 410f), f1 = U(r, 560f, 690f), f2 = f0 * U(r, 1.0f, 1.18f), vib = U(r, 4.6f, 6.0f);
            double ph = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, u = t / dur;
                float f = u < 0.34f ? Mathf.Lerp(f0, f1, Mathf.SmoothStep(0f, 1f, u / 0.34f))
                        : u < 0.62f ? Mathf.Lerp(f1, f1 * 1.04f, (u - 0.34f) / 0.28f)
                        : Mathf.Lerp(f1 * 1.04f, f2, Mathf.SmoothStep(0f, 1f, (u - 0.62f) / 0.38f));
                f *= 1f + 0.014f * Mathf.Sin(TwoPi * vib * t) * Mathf.Clamp01(u * 4f);
                ph += TwoPi * f / Rate;
                float tone = (float)(Math.Sin(ph) + 0.45 * Math.Sin(2 * ph + 0.4) + 0.2 * Math.Sin(3 * ph + 1.1) + 0.07 * Math.Sin(4 * ph));
                float env = Mathf.SmoothStep(0f, 1f, t / 0.5f) * (1f - Mathf.SmoothStep(0f, 1f, (t - (dur - 1f)) / 1f));
                s[i] = lp.Run((tone * 0.6f + breath.Run(N(r)) * 0.25f) * env);
            }
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Il crepitio di un fuoco: un rombo basso che respira, un centinaio di scoppiettii secchi e qualche schiocco piu' forte (un ramo che cede). Loop di 7 s.</summary>
        public static float[] FireCrackle(int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(7.5f);
            var lp = Biquad.LowPass(380f, 0.7f);
            float brown = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                brown = brown * 0.996f + N(r) * 0.06f;
                float t = (float)i / Rate;
                s[i] = lp.Run(brown) * (0.65f + 0.35f * Mathf.Sin(TwoPi * t / 1.9f)) * 1.4f;
            }
            for (int k = 0; k < 170; k++)
            {
                float g = U(r, 0.1f, 1f); g *= g;
                Burst(s, U(r, 0f, 7.3f), U(r, 1400f, 5200f), U(r, 1.2f, 3f), U(r, 0.0012f, 0.006f), g * 1.1f, r);
            }
            for (int k = 0; k < 9; k++)
                Burst(s, U(r, 0f, 7.3f), U(r, 700f, 1500f), 1.6f, U(r, 0.004f, 0.012f), U(r, 0.6f, 1f), r);
            s = LoopSeamless(s, Rate / 2);
            Normalize(s, 0.75f);
            return s;
        }

        /// <summary>Un passo nell'acqua bassa (le pozze delle saline): il tonfo del piede (rumore passato per un filtro basso), lo sciacquio (rumore a banda larga che sfuma piano) e qualche goccia
        /// che ricade (bolle: seni che salgono di tono). Poco piu' di un terzo di secondo.</summary>
        public static float[] Splash(int seed, bool stroke = false)
        {
            var r = new System.Random(seed);
            var s = Buf(0.42f);
            var thud = Biquad.LowPass(stroke ? 620f : 420f, 0.8f);
            var wash = Biquad.BandPass(stroke ? U(r, 1000f, 1800f) : U(r, 1500f, 2300f), 0.7f);
            var hiss = Biquad.HighPass(3000f, 0.6f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, n = N(r);
                float eThud = Mathf.Clamp01(t / 0.006f) * Mathf.Exp(-t * 28f);
                float eWash = Mathf.Clamp01(t / 0.012f) * Mathf.Exp(-t * (stroke ? 8.2f : 11f));
                s[i] = thud.Run(n) * eThud * (stroke ? 0.8f : 1.6f) + wash.Run(n) * eWash * 0.9f + hiss.Run(n) * eWash * eWash * 0.25f;
            }
            int drops = r.Next(5, 9);
            for (int k = 0; k < drops; k++)
            {
                float at = U(r, 0.03f, 0.30f), f0 = U(r, 1100f, 2600f), len = U(r, 0.025f, 0.05f), g = U(r, 0.12f, 0.35f);
                int i0 = (int)(at * Rate), n = (int)(len * Rate);
                double ph = 0;
                for (int i = 0; i < n && i0 + i < s.Length; i++)
                {
                    float u = (float)i / n, env = Mathf.Sin(Mathf.PI * u);
                    ph += TwoPi * f0 * (1f + 1.2f * u) / Rate;
                    s[i0 + i] += (float)Math.Sin(ph) * env * env * g;
                }
            }
            Normalize(s, 0.8f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip sandHiss, crickets, fireCrackle;
        static AudioClip[] caws, howls, splashes, strokes;

        public static AudioClip SandHiss => One(ref sandHiss, "sabbiaSulVento", () => SoundGen.SandHiss(21));
        public static AudioClip Crickets => One(ref crickets, "grilli", () => SoundGen.Crickets(22));
        public static AudioClip FireCrackle => One(ref fireCrackle, "crepitioFuoco", () => SoundGen.FireCrackle(23));
        public static AudioClip Caw => Pick(ref caws, 3, "gracchio", i => SoundGen.Caw(500 + i));
        public static AudioClip Howl => Pick(ref howls, 2, "ululato", i => SoundGen.Howl(600 + i));
        public static AudioClip Splash => Pick(ref splashes, 4, "passoNellAcqua", i => SoundGen.Splash(700 + i));
        public static AudioClip SwimSplash => Pick(ref strokes, 5, "bracciata", i => SoundGen.Splash(740 + i, true));

        /// <summary>Tutti i suoni del deserto, calcolati subito (qualche decina di millisecondi): nessuno scatto alla prima tempesta o al primo grillo.</summary>
        public static void WarmDesert() { _ = SandHiss; _ = Crickets; _ = FireCrackle; _ = Caw; _ = Howl; _ = Splash; _ = SwimSplash; }
    }
}
