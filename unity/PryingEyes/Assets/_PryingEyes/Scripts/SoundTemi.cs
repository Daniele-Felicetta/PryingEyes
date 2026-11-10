using System;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Suoni dei piani a tema (teatro e sanatorio), tutti procedurali come gli altri: nessun file audio.</summary>
    public static partial class SoundGen
    {
        static float[] sawTable;

        /// <summary>Smoothstep tra due soglie: 0 prima di e0, 1 dopo e1.</summary>
        static float SmoothEdge(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Un ciclo di onda a dente di sega morbido (8 armoniche, 1/n), 2048 punti: gli archi dell'orchestra.</summary>
        static float[] SawTable()
        {
            if (sawTable != null) return sawTable;
            var t = new float[2048];
            for (int i = 0; i < t.Length; i++)
            {
                float sum = 0f;
                for (int h = 1; h <= 8; h++) sum += Mathf.Sin(TwoPi * i * h / t.Length) / h;
                t[i] = sum;
            }
            return sawTable = t;
        }

        /// <summary>Orchestra che accorda, dalla fossa, lontana e ovattata: un la sostenuto da cinque archi leggermente scordati con vibrato
        /// lento, un'ottava sotto e una quinta sopra appena accennate, un fruscio d'arco. Passa-basso come attraverso i muri. Loop di circa 12 s.</summary>
        public static float[] TheatreTone()
        {
            var r = new System.Random(31);
            var s = Buf(14f);
            var tab = SawTable();
            const int voices = 5;
            var det = new float[voices]; var vph = new float[voices]; var vrate = new float[voices];
            for (int v = 0; v < voices; v++) { det[v] = 1f + U(r, -0.0045f, 0.0045f); vph[v] = U(r, 0f, 6.28f); vrate[v] = U(r, 4.6f, 5.8f); }
            var ph = new double[voices * 3];
            var lp1 = Biquad.LowPass(1700f, 0.7f); var lp2 = Biquad.LowPass(1700f, 0.7f);
            var air = Biquad.BandPass(2400f, 0.8f);
            float[] mult = { 1f, 0.5f, 1.5f }, amp = { 1f, 0.5f, 0.2f };
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, x = 0f;
                float swell = 0.62f + 0.38f * Mathf.Sin(TwoPi * t / 6.5f + 0.8f);
                for (int v = 0; v < voices; v++)
                {
                    float vib = 1f + 0.004f * Mathf.Sin(TwoPi * vrate[v] * t + vph[v]);
                    for (int o = 0; o < 3; o++)
                    {
                        int k = v * 3 + o;
                        ph[k] += 440.0 * mult[o] * det[v] * vib / Rate;
                        ph[k] -= Math.Floor(ph[k]);
                        x += tab[(int)(ph[k] * 2048.0) & 2047] * amp[o];
                    }
                }
                float bow = air.Run(N(r)) * 0.06f;
                s[i] = lp2.Run(lp1.Run((x * 0.06f + bow) * swell));
            }
            s = LoopSeamless(s, Rate * 2);
            Normalize(s, 0.45f);
            return s;
        }

        /// <summary>Brusio di una sala piena, ma lontano e senza parole: nove voci a banda stretta con sillabe lente che vanno e vengono. Loop di 11 s.</summary>
        public static float[] Murmur()
        {
            var r = new System.Random(32);
            var s = Buf(12f);
            const int voices = 9;
            var f1 = new Biquad[voices]; var f2 = new Biquad[voices];
            var rate = new float[voices]; var ph = new float[voices]; var gateRate = new float[voices]; var gph = new float[voices];
            for (int v = 0; v < voices; v++)
            {
                f1[v] = Biquad.BandPass(U(r, 320f, 700f), 2.2f);
                f2[v] = Biquad.BandPass(U(r, 1100f, 2100f), 3f);
                rate[v] = U(r, 2.6f, 5.2f); ph[v] = U(r, 0f, 6.28f);
                gateRate[v] = 1f / U(r, 3f, 9f); gph[v] = U(r, 0f, 6.28f);
            }
            var far = Biquad.LowPass(1500f, 0.7f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, sum = 0f;
                for (int v = 0; v < voices; v++)
                {
                    float syl = Mathf.Max(0f, Mathf.Sin(TwoPi * rate[v] * t + ph[v]));
                    float gate = Mathf.SmoothStep(0.15f, 0.85f, 0.5f + 0.5f * Mathf.Sin(TwoPi * gateRate[v] * t + gph[v]));
                    float n = N(r);
                    sum += (f1[v].Run(n) + f2[v].Run(n) * 0.5f) * syl * syl * gate;
                }
                s[i] = far.Run(sum);
            }
            s = LoopSeamless(s, Rate);
            Normalize(s, 0.4f);
            return s;
        }

        /// <summary>Applauso di una platea piena sentito da lontano: centinaia di schiocchi filtrati che si addensano, restano e si diradano. 7 s.</summary>
        public static float[] Applause()
        {
            var r = new System.Random(33);
            float dur = 7f;
            var s = Buf(dur);
            int claps = (int)(dur * 240f);
            for (int k = 0, tries = 0; k < claps && tries < claps * 40; tries++)
            {
                float t = U(r, 0f, dur * 0.94f);
                float dens = SmoothEdge(0f, 0.9f, t) * (1f - SmoothEdge(dur * 0.42f, dur * 0.95f, t)) * (0.8f + 0.2f * Mathf.Sin(t * 3.1f));
                if (r.NextDouble() > dens) continue;
                Burst(s, t, U(r, 1100f, 3800f), U(r, 0.9f, 2f), U(r, 0.004f, 0.011f), U(r, 0.25f, 0.9f), r);
                k++;
            }
            var body = Biquad.BandPass(900f, 0.6f); var lp = Biquad.LowPass(4200f, 0.7f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = SmoothEdge(0f, 1.1f, t) * (1f - SmoothEdge(dur * 0.45f, dur * 0.97f, t));
                s[i] = lp.Run(s[i] + body.Run(N(r)) * 0.18f * env);
            }
            Normalize(s, 0.7f);
            return s;
        }

        /// <summary>Campanello di sala: tre squilli rapidi di un campanello acuto di ottone.</summary>
        public static float[] TheatreBell()
        {
            var s = Buf(3.4f);
            float[] ratios = { 1f, 2.76f, 5.4f, 8.9f }, amps = { 1f, 0.5f, 0.25f, 0.12f };
            for (int k = 0; k < 3; k++) Chime(s, 0.05f + k * 0.62f, 1318f, 0.55f, ratios, amps, 0.9f);
            Normalize(s, 0.6f);
            return s;
        }

        /// <summary>Ronzio dei neon: il ronzio a 100 Hz del reattore con le sue armoniche, un battimento lento e un filo acuto. Loop di 8 s.</summary>
        public static float[] ClinicBuzz()
        {
            var r = new System.Random(34);
            var s = Buf(9f);
            var hiss = Biquad.HighPass(5000f, 0.7f);
            float p1 = U(r, 0f, 6f), p2 = U(r, 0f, 6f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, v = 0f;
                for (int h = 1; h <= 9; h++) v += Mathf.Sin(TwoPi * 100f * h * t) / Mathf.Pow(h, 0.9f) * (h % 2 == 1 ? 1f : 0.7f);
                float am = 0.82f + 0.12f * Mathf.Sin(TwoPi * 0.7f * t + p1) + 0.06f * Mathf.Sin(TwoPi * 0.23f * t + p2);
                s[i] = v * 0.12f * am + hiss.Run(N(r)) * 0.02f + Mathf.Sin(TwoPi * 7800f * t) * 0.004f * (0.5f + 0.5f * Mathf.Sin(TwoPi * 0.31f * t));
            }
            s = LoopSeamless(s, Rate);
            Normalize(s, 0.35f);
            return s;
        }

        /// <summary>Carrello d'ospedale che cigola: una ruota con un gemito acuto a ritmo di giro, il rotolio sul linoleum. 2,8 s.</summary>
        public static float[] TrolleySqueak()
        {
            var r = new System.Random(35);
            var s = Buf(2.8f);
            var roll = Biquad.LowPass(220f, 0.8f);
            double ph = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 2.8f));
                float turn = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * t / 0.31f)), 2.5f);        // un giro di ruota ogni 0,31 s
                // gemito della ruota: tono acuto che sale a ogni giro, con due armoniche e un leggero tremolo
                float f = 2000f + 320f * turn + 55f * Mathf.Sin(TwoPi * 7f * t);
                ph += TwoPi * f / Rate;
                float squeal = (Mathf.Sin((float)ph) + 0.45f * Mathf.Sin(2f * (float)ph) + 0.18f * Mathf.Sin(3f * (float)ph)) * turn * 0.55f;
                float rumble = roll.Run(N(r)) * (0.5f + 0.5f * turn) * 1.4f;
                s[i] = (squeal + rumble) * env;
            }
            Normalize(s, 0.6f);
            return s;
        }

        /// <summary>Monitor cardiaco lontano: otto bip puliti a 960 Hz a distanza regolare, ovattati. 8 s.</summary>
        public static float[] MonitorBeep()
        {
            var s = Buf(8f);
            for (int k = 0; k < 8; k++)
            {
                int i0 = (int)((0.2f + k * 0.95f) * Rate), n = (int)(0.18f * Rate);
                for (int j = 0; j < n && i0 + j < s.Length; j++)
                {
                    float t = (float)j / Rate;
                    float env = Mathf.Clamp01(t / 0.004f) * (t < 0.1f ? 1f : Mathf.Exp(-(t - 0.1f) / 0.012f));
                    s[i0 + j] += Mathf.Sin(TwoPi * 960f * t) * env * 0.8f;
                }
            }
            var lp = Biquad.LowPass(2600f, 0.7f);
            for (int i = 0; i < s.Length; i++) s[i] = lp.Run(s[i]);
            Normalize(s, 0.5f);
            return s;
        }

        /// <summary>Portone di ferro che sbatte in fondo a un corridoio: colpo sordo, anello metallico e due echi. 2,4 s.</summary>
        public static float[] IronClang()
        {
            var r = new System.Random(36);
            var s = Buf(2.4f);
            Thump(s, 0f, 140f, 60f, 0.03f, 0.12f, 0.9f);
            Burst(s, 0f, 1600f, 0.8f, 0.02f, 1.1f, r);
            Chime(s, 0f, 380f, 0.5f, new[] { 1f, 2.32f, 3.87f, 5.6f }, new[] { 1f, 0.7f, 0.4f, 0.25f }, 0.7f);
            var dry = (float[])s.Clone();
            foreach (var (delay, gain) in new[] { (0.13f, 0.38f), (0.29f, 0.2f) })
            {
                int d = (int)(delay * Rate);
                for (int i = d; i < s.Length; i++) s[i] += dry[i - d] * gain;
            }
            Normalize(s, 0.75f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip theatreTone, murmur, applause, theatreBell, clinicBuzz, trolley, monitor, clang;

        public static AudioClip TheatreTone => One(ref theatreTone, "tonoTeatro", SoundGen.TheatreTone);
        public static AudioClip Murmur => One(ref murmur, "brusio", SoundGen.Murmur);
        public static AudioClip Applause => One(ref applause, "applauso", SoundGen.Applause);
        public static AudioClip TheatreBell => One(ref theatreBell, "campanelloSala", SoundGen.TheatreBell);
        public static AudioClip ClinicBuzz => One(ref clinicBuzz, "ronzioNeon", SoundGen.ClinicBuzz);
        public static AudioClip Trolley => One(ref trolley, "carrello", SoundGen.TrolleySqueak);
        public static AudioClip Monitor => One(ref monitor, "monitor", SoundGen.MonitorBeep);
        public static AudioClip Clang => One(ref clang, "portoneFerro", SoundGen.IronClang);

        /// <summary>Prepara i suoni dei temi all'avvio, cosi' il primo ingresso in una sala non fa scattare il gioco.</summary>
        public static void WarmThemes()
        {
            _ = TheatreTone; _ = Murmur; _ = Applause; _ = TheatreBell;
            _ = ClinicBuzz; _ = Trolley; _ = Monitor; _ = Clang;
        }
    }
}
