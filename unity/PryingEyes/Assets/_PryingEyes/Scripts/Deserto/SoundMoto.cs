using System;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Il motore della moto (DesertBike), procedurale come gli altri suoni: nessun file audio. Un loop di 1,2 s che si chiude su se stesso perche' ogni frequenza e' un multiplo di 1/1,2 Hz:
    /// 41 scoppi al secondo e quattro quinti (34,2 Hz, un bicilindrico a regime basso), ognuno con il suo colpo secco, le armoniche del tubo di scarico e un fruscio di valvole; il tono sale con la
    /// velocita' (AudioSource.pitch da 0,62 a 2,6 = da circa 21 a 89 Hz) e il volume con il gas. Una seconda voce, il vento di corsa, e' il sibilo della sabbia (SoundBank.SandHiss) che sale
    /// con la velocita'.</summary>
    public static partial class SoundGen
    {
        /// <summary>Il loop del motore. 'seed' cambia solo il rumore (le valvole, lo sfogo dello scarico).</summary>
        public static float[] Engine(int seed)
        {
            const float dur = 1.2f;
            const int pulses = 41;
            var r = new System.Random(seed);
            // si calcolano 'seam' campioni in piu' del giro: la parte tonale e' gia' periodica (ogni frequenza e' un multiplo di 1/1,2 Hz), il rumore invece no, e LoopSeamless
            // fonde la coda in eccesso con l'inizio perche' il punto di giuntura non scatti
            int loop = Mathf.RoundToInt(dur * Rate), seam = 900;
            var s = new float[loop + seam];
            int n = s.Length;
            float f0 = pulses / dur;
            // le armoniche del tubo: la fondamentale e le prime sei, con un'accentuazione sulla seconda e sulla quarta (il suono "pieno" di un bicilindrico) e fase fissa
            float[] amp = { 1.00f, 0.82f, 0.46f, 0.55f, 0.24f, 0.30f, 0.12f };
            float[] ph = { 0.0f, 1.1f, 2.3f, 0.7f, 1.9f, 2.8f, 0.4f };
            var valves = Biquad.BandPass(2600f, 1.6f);
            var rumble = Biquad.LowPass(180f, 0.8f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float u = t * f0 - Mathf.Floor(t * f0);                         // 0..1 dentro uno scoppio
                float env = Mathf.Exp(-u * 5.5f);                                 // il colpo: sale subito, scende in un quinto di scoppio
                float kick = Mathf.Clamp01(u / 0.035f);                           // attacco di pochi millisecondi: non uno scatto
                float body = 0f;
                for (int k = 0; k < amp.Length; k++) body += amp[k] * (float)Math.Sin(TwoPi * f0 * (k + 1) * t + ph[k]);
                float shape = 0.30f + 0.70f * env * kick;
                float noise = N(r);
                float v = body * shape * 0.20f + rumble.Run(noise) * 1.7f * env * kick + valves.Run(noise) * 0.10f * (0.35f + 0.65f * env);
                s[i] = v;
            }
            // lo sfogo dello scarico: qualche scoppiettio piu' forte a posti casuali, lontano dalla giuntura (uno scoppio dura 84 ms al massimo)
            for (int k = 0; k < 5; k++) Burst(s, U(r, 0.05f, dur - 0.12f), U(r, 380f, 900f), 1.4f, 0.012f, 0.16f, r);
            SoftClip(s, 1.6f);
            var o = LoopSeamless(s, seam);
            Normalize(o, 0.75f);
            return o;
        }

        /// <summary>Il tonfo della moto che cade o urta (un colpo sordo di lamiera e sabbia): una volta sola.</summary>
        public static float[] BikeCrash(int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(0.9f);
            Thump(s, 0f, 120f, 42f, 0.14f, 0.11f, 0.95f);
            Burst(s, 0.00f, 900f, 0.9f, 0.05f, 0.55f, r);
            Burst(s, 0.02f, 2200f, 1.2f, 0.035f, 0.35f, r);
            Burst(s, 0.09f, 420f, 1.0f, 0.10f, 0.30f, r);
            Burst(s, 0.21f, 1500f, 1.4f, 0.04f, 0.18f, r);
            SoftClip(s, 1.5f);
            Normalize(s, 0.85f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip engine, bikeCrash;

        /// <summary>Il loop del motore della moto.</summary>
        public static AudioClip Engine => One(ref engine, "motoMotore", () => SoundGen.Engine(31));
        /// <summary>L'urto o l'atterraggio duro della moto.</summary>
        public static AudioClip BikeCrash => One(ref bikeCrash, "motoUrto", () => SoundGen.BikeCrash(32));
    }
}
