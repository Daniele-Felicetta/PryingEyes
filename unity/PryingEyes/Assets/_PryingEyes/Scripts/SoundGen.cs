using System;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Suoni procedurali, nessun file audio: filtri biquad, inviluppi, rumore e i singoli suoni del gioco.
    /// Ogni generatore restituisce un array di campioni mono; usa System.Random e niente API di Unity che non siano
    /// matematica, cosi' i suoni lunghi (il valzer) si possono calcolare in un thread.</summary>
    public static class SoundGen
    {
        public const int Rate = 44100;

        // ---------- filtri ----------
        /// <summary>Biquad RBJ. E' una struct: va tenuta in una variabile e fatta girare campione per campione.</summary>
        public struct Biquad
        {
            float b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public static Biquad LowPass(float f, float q = 0.707f, int rate = Rate) { var b = new Biquad(); b.SetLowPass(f, q, rate); return b; }
            public static Biquad HighPass(float f, float q = 0.707f, int rate = Rate) { var b = new Biquad(); b.SetHighPass(f, q, rate); return b; }
            public static Biquad BandPass(float f, float q, int rate = Rate) { var b = new Biquad(); b.SetBandPass(f, q, rate); return b; }

            void Coefs(float f, float q, int rate, out float c, out float alpha)
            {
                float w = 2f * Mathf.PI * Mathf.Clamp(f, 10f, rate * 0.45f) / rate;
                c = Mathf.Cos(w);
                alpha = Mathf.Sin(w) / (2f * Mathf.Max(q, 0.05f));
            }

            public void SetLowPass(float f, float q, int rate)
            {
                Coefs(f, q, rate, out float c, out float al);
                float a0 = 1f + al;
                b0 = (1f - c) * 0.5f / a0; b1 = (1f - c) / a0; b2 = b0;
                a1 = -2f * c / a0; a2 = (1f - al) / a0;
            }

            public void SetHighPass(float f, float q, int rate)
            {
                Coefs(f, q, rate, out float c, out float al);
                float a0 = 1f + al;
                b0 = (1f + c) * 0.5f / a0; b1 = -(1f + c) / a0; b2 = b0;
                a1 = -2f * c / a0; a2 = (1f - al) / a0;
            }

            /// <summary>Passa-banda con guadagno 0 dB al picco.</summary>
            public void SetBandPass(float f, float q, int rate)
            {
                Coefs(f, q, rate, out float c, out float al);
                float a0 = 1f + al;
                b0 = al / a0; b1 = 0f; b2 = -al / a0;
                a1 = -2f * c / a0; a2 = (1f - al) / a0;
            }

            public float Run(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }

        // ---------- utilita' ----------
        static float[] Buf(float seconds, int rate = Rate) => new float[Mathf.CeilToInt(seconds * rate)];
        static float N(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);
        static float U(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        const float TwoPi = Mathf.PI * 2f;

        public static void Normalize(float[] s, float peak)
        {
            float m = 0f;
            for (int i = 0; i < s.Length; i++) { float a = Mathf.Abs(s[i]); if (a > m) m = a; }
            if (m < 1e-6f || float.IsNaN(m)) return;
            float k = peak / m;
            for (int i = 0; i < s.Length; i++) s[i] *= k;
        }

        static void SoftClip(float[] s, float drive)
        {
            float norm = 1f / (float)Math.Tanh(drive);
            for (int i = 0; i < s.Length; i++) s[i] = (float)Math.Tanh(s[i] * drive) * norm;
        }

        /// <summary>Fa coincidere fine e inizio (dissolvenza incrociata di n campioni): il loop non scatta.</summary>
        static float[] LoopSeamless(float[] s, int n)
        {
            var o = new float[s.Length - n];
            Array.Copy(s, o, o.Length);
            for (int i = 0; i < n; i++)
            {
                float w = (float)i / n;
                o[i] = s[i] * w + s[s.Length - n + i] * (1f - w);
            }
            return o;
        }

        /// <summary>Breve scoppio di rumore filtrato (click, schiocchi, tonfi di legno).</summary>
        static void Burst(float[] s, float at, float freq, float q, float decay, float gain, System.Random r, int rate = Rate)
        {
            var bp = Biquad.BandPass(freq, q, rate);
            int i0 = (int)(at * rate), n = (int)(decay * 7f * rate);
            for (int k = 0; k < n && i0 + k < s.Length; k++)
                s[i0 + k] += bp.Run(N(r)) * Mathf.Exp(-(float)k / rate / decay) * gain;
        }

        /// <summary>Sinusoide smorzata, con una discesa di frequenza opzionale (tonfi).</summary>
        static void Thump(float[] s, float at, float f0, float f1, float glide, float decay, float gain, int rate = Rate)
        {
            int i0 = (int)(at * rate), n = (int)(decay * 7f * rate);
            double ph = 0;
            for (int k = 0; k < n && i0 + k < s.Length; k++)
            {
                float t = (float)k / rate;
                float f = Mathf.Lerp(f0, f1, Mathf.Clamp01(t / glide));
                ph += TwoPi * f / rate;
                float att = Mathf.Clamp01(t / 0.002f);
                s[i0 + k] += (float)Math.Sin(ph) * att * Mathf.Exp(-t / decay) * gain;
            }
        }

        /// <summary>Nota da campanello: parziali con decadimento piu' rapido per quelle alte.</summary>
        static void Chime(float[] s, float at, float f, float decay, float[] ratios, float[] amps, float gain, int rate = Rate)
        {
            int i0 = (int)(at * rate), n = (int)(decay * 6f * rate);
            for (int k = 0; k < n && i0 + k < s.Length; k++)
            {
                float t = (float)k / rate, v = 0f;
                for (int p = 0; p < ratios.Length; p++)
                {
                    float fp = f * ratios[p];
                    if (fp > rate * 0.45f) continue;
                    // argomento in double: a 5 kHz per qualche secondo il float perde precisione
                    v += amps[p] * (float)Math.Sin(2.0 * Math.PI * fp * t + p) * Mathf.Exp(-t / (decay / (1f + 0.8f * p)));
                }
                s[i0 + k] += v * Mathf.Clamp01(t / 0.003f) * gain;
            }
        }

        // ---------- arma e giocatore ----------
        /// <summary>Sparo a strati: schiocco, corpo, colpo basso e coda (l'eco della hall fa il resto).</summary>
        public static float[] Gunshot(int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(0.9f);
            var hp = Biquad.HighPass(2200f, 0.7f);
            var lp = Biquad.LowPass(1700f, 0.8f);
            var tl = Biquad.LowPass(480f, 0.7f);
            double ph = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, n = N(r);
                float crack = hp.Run(n) * Mathf.Exp(-t / 0.0035f) * 1.4f;
                float body = lp.Run(n) * Mathf.Exp(-t / 0.05f) * 1.6f;
                ph += TwoPi * Mathf.Lerp(120f, 42f, Mathf.Clamp01(t / 0.09f)) / Rate;
                float thump = (float)Math.Sin(ph) * Mathf.Exp(-t / 0.075f) * 0.9f;
                float tail = tl.Run(n) * Mathf.Exp(-t / 0.28f) * 0.6f;
                s[i] = crack + body + thump + tail;
            }
            SoftClip(s, 1.6f);
            Normalize(s, 0.95f);
            return s;
        }

        /// <summary>Tic del colpo andato a segno.</summary>
        public static float[] HitTick()
        {
            var r = new System.Random(5);
            var s = Buf(0.07f);
            Burst(s, 0f, 3400f, 2.5f, 0.006f, 1.5f, r);
            for (int i = 0; i < s.Length; i++) { float t = (float)i / Rate; s[i] += Mathf.Sin(TwoPi * 1650f * t) * Mathf.Exp(-t / 0.018f) * 0.4f; }
            Normalize(s, 0.7f);
            return s;
        }

        public static float[] KillThunk()
        {
            var r = new System.Random(6);
            var s = Buf(0.35f);
            Thump(s, 0f, 190f, 80f, 0.12f, 0.09f, 1f);
            Burst(s, 0f, 900f, 0.8f, 0.04f, 1.2f, r);
            Normalize(s, 0.85f);
            return s;
        }

        public static float[] Hurt()
        {
            var r = new System.Random(7);
            var s = Buf(0.45f);
            Thump(s, 0f, 90f, 60f, 0.1f, 0.12f, 1f);
            Burst(s, 0f, 400f, 0.7f, 0.05f, 1.4f, r);
            Burst(s, 0.01f, 220f, 2f, 0.15f, 0.6f, r);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Ricarica in 1,4 s: sgancio del caricatore, inserimento, otturatore.</summary>
        public static float[] Reload()
        {
            var r = new System.Random(8);
            var s = Buf(1.4f);
            Burst(s, 0.05f, 2600f, 3f, 0.012f, 1f, r);
            Chime(s, 0.05f, 3200f, 0.05f, new[] { 1f }, new[] { 0.3f }, 1f);
            Thump(s, 0.72f, 140f, 90f, 0.05f, 0.04f, 0.6f);
            Burst(s, 0.72f, 1900f, 3f, 0.01f, 1f, r);
            Burst(s, 1.15f, 2300f, 4f, 0.008f, 1f, r);
            Burst(s, 1.22f, 1400f, 4f, 0.012f, 0.9f, r);
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Passo: 0 marmo (tacco secco), 1 moquette (tonfo sordo), 2 legno del soppalco.</summary>
        public static float[] Step(int surface, int seed)
        {
            var r = new System.Random(seed * 31 + surface);
            var s = Buf(0.2f);
            if (surface == 1)
            {
                Burst(s, 0f, 300f, 0.7f, 0.045f, 1.4f, r);
                Burst(s, 0f, 900f, 0.8f, 0.012f, 0.4f, r);
            }
            else if (surface == 2)
            {
                Burst(s, 0f, U(r, 420f, 520f), 4f, 0.04f, 1.2f, r);
                Burst(s, 0f, 1800f, 2f, 0.006f, 0.5f, r);
                Thump(s, 0f, 110f, 80f, 0.03f, 0.03f, 0.3f);
            }
            else
            {
                Burst(s, 0f, U(r, 2000f, 2500f), 2f, 0.011f, 1.3f, r);
                Burst(s, 0f, 700f, 0.8f, 0.028f, 0.6f, r);
                Chime(s, 0f, U(r, 1000f, 1250f), 0.05f, new[] { 1f }, new[] { 0.12f }, 1f);
            }
            Normalize(s, 0.8f);
            return s;
        }

        // ---------- hotel ----------
        /// <summary>Campanello della reception: parziali inarmoniche di una campana piccola.</summary>
        public static float[] ReceptionBell()
        {
            var r = new System.Random(9);
            var s = Buf(2.4f);
            Chime(s, 0f, 2000f, 1.5f, new[] { 1f, 2.76f, 5.40f, 0.5f }, new[] { 1f, 0.55f, 0.3f, 0.12f }, 1f);
            var hp = Biquad.HighPass(4000f);
            for (int i = 0; i < 200; i++) s[i] += hp.Run(N(r)) * Mathf.Exp(-i / 90f) * 0.6f;
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>"Ding" dell'ascensore: due note che scendono (mi, do).</summary>
        public static float[] ElevatorDing()
        {
            var s = Buf(2.8f);
            var ratios = new[] { 1f, 2f, 3f };
            var amps = new[] { 1f, 0.22f, 0.07f };
            Chime(s, 0f, 1318.5f, 1.4f, ratios, amps, 1f);
            Chime(s, 0.45f, 1046.5f, 1.7f, ratios, amps, 0.9f);
            Normalize(s, 0.75f);
            return s;
        }

        /// <summary>Porte dell'ascensore che scorrono: rombo meccanico e colpo di fine corsa.</summary>
        public static float[] ElevatorDoors()
        {
            var r = new System.Random(10);
            var s = Buf(1.7f);
            var lp = Biquad.LowPass(220f, 0.8f);
            var lp2 = Biquad.LowPass(900f, 0.7f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Clamp01(t / 0.12f) * (t < 1.25f ? 1f : Mathf.Exp(-(t - 1.25f) / 0.06f));
                float n = N(r);
                s[i] = (lp.Run(n) * 2.2f + lp2.Run(n) * 0.25f + Mathf.Sin(TwoPi * 50f * t) * 0.12f + Mathf.Sin(TwoPi * 100f * t) * 0.06f) * env;
            }
            Thump(s, 1.28f, 85f, 60f, 0.05f, 0.1f, 0.8f);
            Burst(s, 1.28f, 700f, 1f, 0.03f, 0.9f, r);
            Normalize(s, 0.7f);
            return s;
        }

        /// <summary>Scricchiolio di legno: impulsi stick-slip con periodo che rallenta, dentro risonanze strette.</summary>
        public static float[] Creak(int seed)
        {
            var r = new System.Random(seed);
            float dur = U(r, 0.6f, 1.3f);
            var s = Buf(dur);
            var res = Biquad.BandPass(U(r, 480f, 900f), 6f);
            var res2 = Biquad.BandPass(U(r, 1300f, 1900f), 8f);
            var body = Biquad.LowPass(300f, 0.8f);
            float p0 = 1f / U(r, 70f, 130f), p1 = p0 * U(r, 1.3f, 1.9f), next = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, x = 0f;
                if (t >= next)
                {
                    x = 1f;
                    next = t + Mathf.Lerp(p0, p1, t / dur) * U(r, 0.8f, 1.2f);
                }
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                s[i] = (res.Run(x) + res2.Run(x) * 0.5f + body.Run(x) * 0.6f) * env * env;
            }
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Vento dal lucernario rotto: rumore scuro con raffiche lente e un fischio tra i vetri. Loop di 12 s.</summary>
        public static float[] Wind(int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(13f);
            var bp = new Biquad(); var wh = new Biquad();
            var lp = Biquad.LowPass(1200f, 0.5f);
            float p1 = U(r, 0f, 6f), p2 = U(r, 0f, 6f), p3 = U(r, 0f, 6f), p4 = U(r, 0f, 6f), p5 = U(r, 0f, 6f);
            float brown = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, n = N(r);
                brown = brown * 0.985f + n * 0.15f;
                float gust = 0.55f + 0.25f * Mathf.Sin(TwoPi * t / 5.3f + p1) + 0.15f * Mathf.Sin(TwoPi * t / 3.1f + p2)
                             + 0.1f * Mathf.Sin(TwoPi * t / 8.7f + p3);
                if ((i & 63) == 0)
                {
                    bp.SetBandPass(380f + 420f * (0.5f + 0.5f * Mathf.Sin(TwoPi * t / 6.7f + p4)) * gust, 0.9f, Rate);
                    wh.SetBandPass(1250f + 250f * Mathf.Sin(TwoPi * t / 4.3f + p5), 12f, Rate);
                }
                s[i] = bp.Run(brown * 3f) * gust + wh.Run(n) * 0.15f * gust * gust + lp.Run(n) * 0.04f;
            }
            s = LoopSeamless(s, Rate);
            Normalize(s, 0.6f);
            return s;
        }

        /// <summary>Ronzio della stanza vuota: rombo molto basso e un filo di rete elettrica. Loop di 8 s.</summary>
        public static float[] RoomTone()
        {
            var r = new System.Random(11);
            var s = Buf(9f);
            var lp = Biquad.LowPass(80f, 0.7f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                s[i] = lp.Run(N(r)) * 3f + Mathf.Sin(TwoPi * 50f * t) * 0.05f + Mathf.Sin(TwoPi * 100f * t) * 0.02f;
            }
            s = LoopSeamless(s, Rate);
            Normalize(s, 0.3f);
            return s;
        }

        /// <summary>Sussurro (quando gli occhi nel buio si chiudono): fruscio acuto a sillabe.</summary>
        public static float[] Whisper(int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(1.1f);
            var a = Biquad.BandPass(3500f, 1.5f);
            var b = Biquad.BandPass(6000f, 2f);
            float syl = U(r, 5f, 7f), ph = U(r, 0f, 6f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, n = N(r);
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.1f));
                float am = 0.35f + 0.65f * Mathf.Max(0f, Mathf.Sin(TwoPi * syl * t + ph));
                s[i] = (a.Run(n) + b.Run(n) * 0.6f) * env * am;
            }
            Normalize(s, 0.6f);
            return s;
        }

        /// <summary>Valzer da grammofono, originale: la minore, 3/4, 84 bpm, 16 battute (circa 34 s, loop esatto).
        /// Pianoforte additivo, poi "disco vecchio": banda stretta, wow, fruscio e crepitio. Lento: va in un thread.</summary>
        public static float[] Waltz(out int rate)
        {
            rate = 22050;
            float beat = 60f / 84f;
            const int bars = 16;
            int loopLen = Mathf.RoundToInt(bars * 3 * beat * rate);
            var dry = new float[loopLen + rate * 2];
            // melodia: (nota MIDI, battiti) per battuta
            int[][] mn = {
                new[] { 76, 72 }, new[] { 69, 71, 72 }, new[] { 74, 77 }, new[] { 76, 68 },
                new[] { 69, 76 }, new[] { 77, 76, 74 }, new[] { 74, 72 }, new[] { 71 },
                new[] { 72, 76 }, new[] { 74, 71 }, new[] { 72, 71, 69 }, new[] { 68, 71 },
                new[] { 69, 72 }, new[] { 77, 76, 74 }, new[] { 76, 74, 71 }, new[] { 69 } };
            float[][] md = {
                new[] { 2f, 1f }, new[] { 1f, 1f, 1f }, new[] { 2f, 1f }, new[] { 2f, 1f },
                new[] { 2f, 1f }, new[] { 1f, 1f, 1f }, new[] { 2f, 1f }, new[] { 3f },
                new[] { 2f, 1f }, new[] { 2f, 1f }, new[] { 1f, 1f, 1f }, new[] { 2f, 1f },
                new[] { 2f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 3f } };
            int[] bass = { 45, 45, 50, 40, 45, 41, 50, 40, 48, 43, 45, 40, 41, 50, 40, 45 };
            int[][] ch = {
                new[] { 57, 60, 64 }, new[] { 57, 60, 64 }, new[] { 57, 62, 65 }, new[] { 56, 59, 62 },
                new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 57, 62, 65 }, new[] { 56, 59, 64 },
                new[] { 55, 60, 64 }, new[] { 55, 59, 62 }, new[] { 57, 60, 64 }, new[] { 56, 59, 62 },
                new[] { 53, 57, 60 }, new[] { 57, 62, 65 }, new[] { 56, 59, 62 }, new[] { 57, 60, 64 } };
            for (int b = 0; b < bars; b++)
            {
                float t0 = b * 3 * beat;
                PianoNote(dry, rate, t0, bass[b], 1.2f * beat, 0.5f, true);
                foreach (int n in ch[b])
                {
                    PianoNote(dry, rate, t0 + beat, n, 0.8f * beat, 0.16f, false);
                    PianoNote(dry, rate, t0 + 2 * beat, n, 0.8f * beat, 0.13f, false);
                }
                float mt = t0;
                for (int k = 0; k < mn[b].Length; k++)
                {
                    PianoNote(dry, rate, mt, mn[b][k], md[b][k] * beat, 0.42f, false);
                    mt += md[b][k] * beat;
                }
            }
            // la coda delle ultime note torna all'inizio: il loop non ha buchi
            for (int i = loopLen; i < dry.Length; i++) dry[i - loopLen] += dry[i];

            // disco vecchio: wow (linea di ritardo modulata con periodi interi nel loop), banda 300-3200 Hz,
            // fruscio, crepitio, un filo di saturazione. Filtri fatti girare su due giri: niente salto al loop.
            var r = new System.Random(12);
            float loopSec = (float)loopLen / rate;
            float f1 = 19f / loopSec, f2 = 4f / loopSec;
            var wet = new float[loopLen];
            var hp1 = Biquad.HighPass(300f, 0.7f, rate); var hp2 = Biquad.HighPass(300f, 0.7f, rate);
            var lp1 = Biquad.LowPass(3200f, 0.7f, rate); var lp2 = Biquad.LowPass(3200f, 0.7f, rate);
            var hissF = Biquad.HighPass(4000f, 0.7f, rate);
            var popF = Biquad.HighPass(1800f, 0.7f, rate);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < loopLen; i++)
                {
                    float t = (float)i / rate;
                    float delay = (0.005f + 0.0009f * Mathf.Sin(TwoPi * f1 * t) + 0.0006f * Mathf.Sin(TwoPi * f2 * t)) * rate;
                    float pos = i - delay;
                    pos = (pos % loopLen + loopLen) % loopLen;
                    int i0 = (int)pos; float fr = pos - i0;
                    float x = dry[i0] * (1f - fr) + dry[(i0 + 1) % loopLen] * fr;
                    x = lp2.Run(lp1.Run(hp2.Run(hp1.Run(x))));
                    float pop = 0f;
                    double u = r.NextDouble();
                    if (u < 8.0 / rate) pop = N(r) * U(r, 0.05f, 0.15f);
                    else if (u < 8.5 / rate) pop = N(r) * 0.35f;
                    x += popF.Run(pop) + hissF.Run(N(r)) * 0.012f;
                    if (pass == 1) wet[i] = (float)Math.Tanh(x * 1.3f);
                }
            Normalize(wet, 0.7f);
            return wet;
        }

        static void PianoNote(float[] buf, int rate, float at, int midi, float dur, float gain, bool bass)
        {
            float f = 440f * Mathf.Pow(2f, (midi - 69) / 12f);
            int i0 = Mathf.RoundToInt(at * rate);
            int n = Mathf.RoundToInt((dur + (bass ? 0.6f : 0.45f)) * rate);
            float dec = bass ? 1.8f : 2.6f;
            for (int k = 0; k < n && i0 + k < buf.Length; k++)
            {
                float t = (float)k / rate;
                float env = Mathf.Clamp01(t / 0.005f) * Mathf.Exp(-t * dec) * (t > dur ? Mathf.Exp(-(t - dur) / 0.08f) : 1f);
                float w = TwoPi * f * t;
                float v = Mathf.Sin(w) + 0.45f * Mathf.Sin(2f * w) * Mathf.Exp(-t * 3f)
                          + 0.22f * Mathf.Sin(3f * w) * Mathf.Exp(-t * 5f) + 0.1f * Mathf.Sin(4f * w) * Mathf.Exp(-t * 7f);
                buf[i0 + k] += v * env * gain;
            }
        }

        // ---------- nemici ----------
        /// <summary>Litania a bocca chiusa del monaco ("mmm-ooo"): armoniche sagomate da due formanti, piccolo vibrato,
        /// frase modale lenta con respiri. Loop di 9 s; ogni monaco la suona a un'altezza diversa: coro stonato.</summary>
        public static float[] MonkChant(int seed, out int rate)
        {
            rate = 22050;
            var r = new System.Random(seed);
            (float midi, float dur)[] ph = { (50f, 1.6f), (53f, 1.2f), (52f, 1.4f), (50f, 2.0f), (48f, 1.0f), (50f, 1.8f) };
            float total = 0f; foreach (var p in ph) total += p.dur;
            float len = total + 0.5f;
            var s = new float[Mathf.CeilToInt(len * rate)];
            var breath = Biquad.BandPass(700f, 1f, rate);
            double phase = 0;
            float vib = U(r, 4.8f, 5.6f), vibPh = U(r, 0f, 6f);
            const int K = 14;
            var form = new float[K + 1];
            float breathAt = ph[0].dur + ph[1].dur + ph[2].dur + ph[3].dur;   // respiro prima della quinta nota
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / rate;
                float tt = t % total, acc = 0f; int idx = 0;
                while (idx < ph.Length - 1 && tt >= acc + ph[idx].dur) { acc += ph[idx].dur; idx++; }
                float local = tt - acc;
                float prevMidi = ph[(idx + ph.Length - 1) % ph.Length].midi;
                float midi = Mathf.Lerp(prevMidi, ph[idx].midi, Mathf.SmoothStep(0f, 1f, local / 0.18f));
                float f0 = 440f * Mathf.Pow(2f, (midi - 69f) / 12f) * (1f + 0.0035f * Mathf.Sin(TwoPi * vib * t + vibPh));
                phase += TwoPi * f0 / rate;
                if (phase > TwoPi) phase -= TwoPi;   // le armoniche sono multipli interi: si puo' riavvolgere
                // inviluppo continuo: a fine nota scende al 65% e la nota dopo riparte da li' (prima ripartiva da
                // zero: un click a ogni cambio di nota). Un respiro vero, morbido, a meta' frase.
                float rise = Mathf.Lerp(0.65f, 1f, Mathf.SmoothStep(0f, 1f, local / 0.25f));
                float fall = 1f - 0.35f * Mathf.SmoothStep(0f, 1f, (local - ph[idx].dur + 0.35f) / 0.35f);
                float bx = (tt - breathAt) / 0.28f;
                float env = Mathf.Min(rise, fall) * (1f - 0.85f * Mathf.Exp(-bx * bx));
                if ((i & 31) == 0)
                {
                    // formanti ricalcolate ogni 32 campioni: cambiano lentamente, e cosi' costa un decimo
                    float morph = 0.5f + 0.5f * Mathf.Sin(TwoPi * t / 4.5f);       // mmm <-> ooo
                    float fA = Mathf.Lerp(250f, 330f, morph), fB = Mathf.Lerp(700f, 880f, morph);
                    for (int k = 1; k <= K; k++)
                    {
                        float fk = f0 * k;
                        form[k] = fk > 1600f ? 0f
                            : (Mathf.Exp(-Sq((fk - fA) / 140f)) + 0.45f * Mathf.Exp(-Sq((fk - fB) / 220f)) + 0.08f) / k;
                    }
                }
                float v = 0f;
                for (int k = 1; k <= K; k++)
                    if (form[k] > 0f) v += Mathf.Sin((float)(phase * k)) * form[k];
                s[i] = (v + breath.Run(N(r)) * 0.05f) * env;
            }
            s = LoopSeamless(s, rate / 2);
            Normalize(s, 0.7f);
            return s;
        }

        static float Sq(float x) => x * x;

        /// <summary>Colpo del bastone sul pavimento di legno/marmo: tonfo secco.</summary>
        public static float[] StaffTap(int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(0.3f);
            Burst(s, 0f, U(r, 620f, 760f), 6f, 0.03f, 1.2f, r);
            Thump(s, 0f, 200f, 170f, 0.03f, 0.05f, 0.5f);
            Burst(s, 0f, 2500f, 3f, 0.004f, 0.4f, r);
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Il bastone piantato a terra: tonfo pesante, schiocco e il "whoom" dell'onda blu.</summary>
        public static float[] StaffStrike()
        {
            var r = new System.Random(13);
            var s = Buf(1.1f);
            Thump(s, 0f, 60f, 35f, 0.2f, 0.25f, 1f);
            Burst(s, 0f, 1500f, 2f, 0.02f, 1f, r);
            var lp = new Biquad();
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                if ((i & 31) == 0) lp.SetLowPass(Mathf.Lerp(2000f, 200f, Mathf.Clamp01(t / 0.6f)), 1.2f, Rate);
                float env = Mathf.Clamp01(t / 0.05f) * Mathf.Exp(-t / 0.25f);
                s[i] += lp.Run(N(r)) * env * 0.9f;
            }
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Il monaco crolla: fruscio di stoffa, tonfo a terra, un respiro che esce.</summary>
        public static float[] MonkDeath()
        {
            var r = new System.Random(14);
            var s = Buf(1.5f);
            var cloth = Biquad.LowPass(700f, 0.7f);
            var exhale = Biquad.BandPass(420f, 1.2f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, n = N(r);
                float c = t < 0.6f ? Mathf.Sin(Mathf.PI * t / 0.6f) : 0f;
                float e = Mathf.Clamp01((t - 0.15f) / 0.2f) * Mathf.Exp(-Mathf.Max(0f, t - 0.35f) / 0.35f);
                s[i] = cloth.Run(n) * c * 1.2f + exhale.Run(n) * e * 0.7f;
            }
            Thump(s, 0.62f, 75f, 50f, 0.08f, 0.12f, 0.9f);
            Normalize(s, 0.85f);
            return s;
        }

        /// <summary>Ronzio del drone: rotore a 118 Hz modulato a 23 Hz e un fruscio d'aria. Loop esatto di 2 s.</summary>
        public static float[] DroneBuzz()
        {
            var r = new System.Random(15);
            int n = Rate * 2;
            var s = new float[n];
            var lp = Biquad.LowPass(3000f, 0.7f);
            var bp = Biquad.BandPass(600f, 1.5f);
            var air = Biquad.BandPass(2500f, 0.8f);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / Rate;
                    float saw = 2f * ((118f * t) % 1f) - 1f;
                    float am = 0.7f + 0.3f * Mathf.Sin(TwoPi * 23f * t);
                    float x = lp.Run(saw);
                    float v = (x * 0.5f + bp.Run(x) * 1.2f) * am + air.Run(N(r)) * 0.25f;
                    if (pass == 1) s[i] = v;
                }
            Normalize(s, 0.6f);
            return s;
        }

        /// <summary>Passo del bruto corazzato: tonfo basso e un clangore di piastre.</summary>
        public static float[] BruteStep(int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(0.35f);
            Thump(s, 0f, 65f, 45f, 0.06f, 0.08f, 1f);
            Burst(s, 0f, 300f, 0.8f, 0.05f, 1f, r);
            Burst(s, 0.005f, U(r, 1100f, 1400f), 6f, 0.03f, 0.35f, r);
            Normalize(s, 0.85f);
            return s;
        }
    }
}
