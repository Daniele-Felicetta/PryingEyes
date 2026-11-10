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

        // ---------- spari per classe di arma ----------
        struct GP { public float hp, crackD, lp, bodyD, f0, f1, thumpD, tl, tailD, dur, crack, body, thump, tail, drive; }

        static GP Profile(string set)
        {
            switch (set)
            {
                case "pistol":  return new GP { hp = 3000, crackD = 0.0025f, lp = 2400, bodyD = 0.032f, f0 = 170, f1 = 80, thumpD = 0.04f, tl = 900, tailD = 0.16f, dur = 0.55f, crack = 1.5f, body = 1.3f, thump = 0.7f, tail = 0.5f, drive = 1.6f };
                case "pistol45": return new GP { hp = 2600, crackD = 0.003f, lp = 1700, bodyD = 0.045f, f0 = 130, f1 = 60, thumpD = 0.06f, tl = 650, tailD = 0.2f, dur = 0.6f, crack = 1.3f, body = 1.5f, thump = 0.95f, tail = 0.55f, drive = 1.7f };
                case "revolver": return new GP { hp = 2300, crackD = 0.0035f, lp = 1300, bodyD = 0.07f, f0 = 95, f1 = 40, thumpD = 0.11f, tl = 520, tailD = 0.32f, dur = 0.95f, crack = 1.4f, body = 1.6f, thump = 1.2f, tail = 0.75f, drive = 1.9f };
                case "magnum":  return new GP { hp = 2000, crackD = 0.004f, lp = 1000, bodyD = 0.09f, f0 = 72, f1 = 30, thumpD = 0.16f, tl = 420, tailD = 0.42f, dur = 1.1f, crack = 1.4f, body = 1.8f, thump = 1.5f, tail = 0.9f, drive = 2.1f };
                case "smg":     return new GP { hp = 3200, crackD = 0.0022f, lp = 2600, bodyD = 0.028f, f0 = 150, f1 = 85, thumpD = 0.035f, tl = 1000, tailD = 0.13f, dur = 0.42f, crack = 1.5f, body = 1.2f, thump = 0.6f, tail = 0.45f, drive = 1.5f };
                case "smg45":   return new GP { hp = 2800, crackD = 0.0026f, lp = 1900, bodyD = 0.038f, f0 = 125, f1 = 62, thumpD = 0.05f, tl = 750, tailD = 0.16f, dur = 0.48f, crack = 1.4f, body = 1.4f, thump = 0.85f, tail = 0.5f, drive = 1.6f };
                case "ak":      return new GP { hp = 1900, crackD = 0.0042f, lp = 1350, bodyD = 0.06f, f0 = 105, f1 = 40, thumpD = 0.085f, tl = 430, tailD = 0.32f, dur = 0.95f, crack = 1.5f, body = 1.7f, thump = 1.0f, tail = 0.65f, drive = 1.8f };
                case "br":      return new GP { hp = 1700, crackD = 0.0048f, lp = 1050, bodyD = 0.07f, f0 = 88, f1 = 33, thumpD = 0.11f, tl = 380, tailD = 0.40f, dur = 1.05f, crack = 1.5f, body = 1.8f, thump = 1.25f, tail = 0.75f, drive = 1.9f };
                case "lever":   return new GP { hp = 2000, crackD = 0.0045f, lp = 1450, bodyD = 0.06f, f0 = 100, f1 = 38, thumpD = 0.09f, tl = 460, tailD = 0.34f, dur = 0.9f, crack = 1.5f, body = 1.6f, thump = 1.0f, tail = 0.7f, drive = 1.8f };
                case "sniper":  return new GP { hp = 1500, crackD = 0.006f, lp = 800, bodyD = 0.09f, f0 = 66, f1 = 28, thumpD = 0.16f, tl = 300, tailD = 0.6f, dur = 1.45f, crack = 1.6f, body = 1.9f, thump = 1.4f, tail = 1.0f, drive = 2.0f };
                case "amr":     return new GP { hp = 1200, crackD = 0.008f, lp = 600, bodyD = 0.12f, f0 = 48, f1 = 22, thumpD = 0.28f, tl = 250, tailD = 0.9f, dur = 1.9f, crack = 1.6f, body = 2.0f, thump = 1.8f, tail = 1.2f, drive = 2.4f };
                case "shotgun": return new GP { hp = 1300, crackD = 0.009f, lp = 950, bodyD = 0.10f, f0 = 72, f1 = 32, thumpD = 0.14f, tl = 360, tailD = 0.5f, dur = 1.05f, crack = 1.2f, body = 2.0f, thump = 1.2f, tail = 0.9f, drive = 2.0f };
                case "lmg":     return new GP { hp = 2000, crackD = 0.0035f, lp = 1450, bodyD = 0.045f, f0 = 95, f1 = 46, thumpD = 0.07f, tl = 460, tailD = 0.25f, dur = 0.75f, crack = 1.4f, body = 1.6f, thump = 1.0f, tail = 0.6f, drive = 1.8f };
                default:        return new GP { hp = 2200, crackD = 0.0035f, lp = 1700, bodyD = 0.05f, f0 = 120, f1 = 42, thumpD = 0.075f, tl = 480, tailD = 0.28f, dur = 0.9f, crack = 1.4f, body = 1.6f, thump = 0.9f, tail = 0.6f, drive = 1.6f };
            }
        }

        /// <summary>Sparo di una classe d'arma: schiocco, corpo, tonfo basso e coda, con parametri diversi per ogni classe.</summary>
        public static float[] Gunshot(string set, int seed)
        {
            var p = Profile(set);
            var r = new System.Random(seed * 17 + set.Length * 101);
            var s = Buf(p.dur);
            var hp = Biquad.HighPass(p.hp * U(r, 0.94f, 1.06f), 0.7f);
            var lp = Biquad.LowPass(p.lp * U(r, 0.94f, 1.06f), 0.8f);
            var tl = Biquad.LowPass(p.tl, 0.7f);
            double ph = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, n = N(r);
                float crack = hp.Run(n) * Mathf.Exp(-t / p.crackD) * p.crack;
                float body = lp.Run(n) * Mathf.Exp(-t / p.bodyD) * p.body;
                ph += TwoPi * Mathf.Lerp(p.f0, p.f1, Mathf.Clamp01(t / (p.thumpD * 1.2f))) / Rate;
                float thump = (float)Math.Sin(ph) * Mathf.Exp(-t / p.thumpD) * p.thump;
                float tail = tl.Run(n) * Mathf.Exp(-t / p.tailD) * p.tail;
                s[i] = crack + body + thump + tail;
            }
            SoftClip(s, p.drive);
            Normalize(s, 0.95f);
            return s;
        }

        /// <summary>Suoni delle armi speciali: ruggito di fiamma (si ripete a raffica), raggio d'energia, spine organiche.</summary>
        public static float[] Special(string set, int seed)
        {
            var r = new System.Random(seed * 23 + set.Length * 37);
            float[] s;
            switch (set)
            {
                case "flame":
                {
                    s = Buf(0.42f);
                    var lp = Biquad.LowPass(1500f, 0.7f); var lp2 = Biquad.LowPass(420f, 0.8f);
                    double ph = 0;
                    for (int i = 0; i < s.Length; i++)
                    {
                        float t = (float)i / Rate, n = N(r);
                        float env = Mathf.Clamp01(t / 0.035f) * Mathf.Exp(-t / 0.17f);
                        ph += TwoPi * (62f + 18f * Mathf.Sin(t * 31f)) / Rate;
                        s[i] = (lp.Run(n) * 1.0f + lp2.Run(n) * 1.4f + (float)Math.Sin(ph) * 0.45f) * env;
                    }
                    SoftClip(s, 1.5f); Normalize(s, 0.85f);
                    return s;
                }
                case "beam":
                {
                    s = Buf(0.95f);
                    double ph = 0, ph2 = 0;
                    var hp = Biquad.HighPass(3200f, 0.7f);
                    for (int i = 0; i < s.Length; i++)
                    {
                        float t = (float)i / Rate, n = N(r);
                        float f = Mathf.Lerp(2600f, 150f, 1f - Mathf.Exp(-t / 0.05f));
                        ph += TwoPi * f / Rate;
                        ph2 += TwoPi * f * 1.5f / Rate;
                        float body = ((float)Math.Sin(ph) + 0.5f * (float)Math.Sin(ph2)) * Mathf.Exp(-t / 0.16f);
                        float crack = hp.Run(n) * Mathf.Exp(-t / 0.012f) * 1.2f;
                        s[i] = body + crack;
                    }
                    Chime(s, 0.01f, 1700f, 0.5f, new[] { 1f, 2.41f, 3.73f, 5.1f }, new[] { 0.35f, 0.22f, 0.14f, 0.08f }, 1f);
                    SoftClip(s, 1.4f); Normalize(s, 0.92f);
                    return s;
                }
                default: // spine
                {
                    s = Buf(0.60f);
                    var bp = Biquad.BandPass(650f, 1.5f); var hp = Biquad.HighPass(5000f, 0.7f); var hp2 = Biquad.HighPass(3200f, 0.7f);
                    double ph = 0;
                    for (int i = 0; i < s.Length; i++)
                    {
                        float t = (float)i / Rate, n = N(r);
                        ph += TwoPi * Mathf.Lerp(125f, 52f, Mathf.Clamp01(t / 0.12f)) / Rate;
                        float thump = (float)Math.Sin(ph) * Mathf.Exp(-t / 0.07f) * 1.1f;
                        float wet = bp.Run(n) * Mathf.Exp(-t / 0.09f) * 1.5f;
                        float hiss = hp.Run(n) * Mathf.Exp(-t / 0.22f) * 0.45f;
                        float crack = hp2.Run(n) * Mathf.Exp(-t / 0.004f) * 1.1f;
                        s[i] = thump + wet + hiss + crack;
                    }
                    SoftClip(s, 1.7f); Normalize(s, 0.92f);
                    return s;
                }
            }
        }

        /// <summary>Rumori meccanici delle armi: caricatori, otturatori, pompe, cilindro, leve, cartucce.</summary>
        public static float[] Mech(string name, int seed)
        {
            var r = new System.Random(seed * 13 + name.Length * 7 + name[0]);
            float[] s;
            switch (name)
            {
                case "magout":
                    s = Buf(0.32f);
                    Burst(s, 0.0f, 2600f, 3f, 0.012f, 1f, r); Chime(s, 0.0f, 3200f, 0.05f, new[] { 1f }, new[] { 0.3f }, 1f);
                    Thump(s, 0.09f, 150f, 80f, 0.05f, 0.05f, 0.5f); Burst(s, 0.09f, 900f, 2f, 0.02f, 0.5f, r);
                    break;
                case "magin":
                    s = Buf(0.30f);
                    Thump(s, 0.0f, 140f, 90f, 0.05f, 0.045f, 0.7f); Burst(s, 0.0f, 1900f, 3f, 0.011f, 1.0f, r);
                    Burst(s, 0.045f, 3100f, 5f, 0.006f, 0.8f, r); Chime(s, 0.04f, 2700f, 0.04f, new[] { 1f, 1.6f }, new[] { 0.3f, 0.15f }, 1f);
                    break;
                case "boltback":
                    s = Buf(0.34f);
                    Burst(s, 0.0f, 1500f, 1.4f, 0.09f, 0.9f, r); Burst(s, 0.14f, 2300f, 4f, 0.01f, 1.0f, r); Thump(s, 0.14f, 150f, 90f, 0.04f, 0.04f, 0.5f);
                    break;
                case "boltfwd":
                    s = Buf(0.32f);
                    Burst(s, 0.0f, 1700f, 1.4f, 0.07f, 0.8f, r); Burst(s, 0.10f, 2000f, 3f, 0.012f, 1.3f, r); Thump(s, 0.10f, 130f, 70f, 0.05f, 0.06f, 0.9f);
                    Chime(s, 0.10f, 2400f, 0.06f, new[] { 1f, 2.3f }, new[] { 0.3f, 0.15f }, 1f);
                    break;
                case "boltup":
                    s = Buf(0.18f);
                    Burst(s, 0.0f, 2800f, 5f, 0.008f, 1.0f, r); Burst(s, 0.03f, 1400f, 2f, 0.04f, 0.5f, r);
                    break;
                case "slidefwd":
                    s = Buf(0.22f);
                    Burst(s, 0.0f, 2300f, 4f, 0.01f, 1.4f, r); Thump(s, 0.0f, 160f, 90f, 0.03f, 0.04f, 0.6f); Chime(s, 0.0f, 3300f, 0.05f, new[] { 1f }, new[] { 0.35f }, 1f);
                    break;
                case "pumpback":
                    s = Buf(0.3f);
                    Burst(s, 0.0f, 1200f, 1.2f, 0.08f, 0.8f, r); Burst(s, 0.11f, 2400f, 4f, 0.01f, 0.9f, r); Thump(s, 0.11f, 140f, 80f, 0.04f, 0.04f, 0.5f);
                    break;
                case "pumpfwd":
                    s = Buf(0.3f);
                    Burst(s, 0.0f, 1300f, 1.2f, 0.06f, 0.7f, r); Burst(s, 0.09f, 1900f, 3f, 0.012f, 1.4f, r); Thump(s, 0.09f, 120f, 65f, 0.05f, 0.07f, 1.0f);
                    Chime(s, 0.09f, 2100f, 0.05f, new[] { 1f, 2.1f }, new[] { 0.25f, 0.1f }, 1f);
                    break;
                case "leverdown":
                    s = Buf(0.34f);
                    Burst(s, 0.0f, 2200f, 2f, 0.03f, 0.6f, r); Burst(s, 0.02f, 1000f, 1.2f, 0.1f, 0.7f, r); Burst(s, 0.16f, 2600f, 4f, 0.01f, 0.9f, r);
                    break;
                case "leverup":
                    s = Buf(0.30f);
                    Burst(s, 0.0f, 1500f, 1.4f, 0.09f, 0.7f, r); Burst(s, 0.12f, 2100f, 3f, 0.012f, 1.4f, r); Thump(s, 0.12f, 130f, 70f, 0.05f, 0.06f, 0.9f);
                    break;
                case "shellin":
                    s = Buf(0.2f);
                    Burst(s, 0.0f, 3400f, 6f, 0.006f, 0.9f, r); Burst(s, 0.0f, 900f, 1.5f, 0.02f, 0.6f, r); Thump(s, 0.012f, 220f, 120f, 0.03f, 0.03f, 0.5f);
                    Chime(s, 0.0f, 4200f, 0.03f, new[] { 1f }, new[] { 0.25f }, 1f);
                    break;
                case "shellopen":
                    s = Buf(0.14f);
                    Burst(s, 0.0f, 2800f, 5f, 0.008f, 0.8f, r);
                    break;
                case "cylopen":
                    s = Buf(0.32f);
                    Burst(s, 0.0f, 2600f, 4f, 0.01f, 1.0f, r);
                    for (int k = 0; k < 5; k++) Burst(s, 0.07f + k * 0.032f, 3300f, 8f, 0.004f, 0.55f, r);
                    Thump(s, 0.0f, 150f, 90f, 0.04f, 0.04f, 0.4f);
                    break;
                case "cylshells":
                    s = Buf(0.28f);
                    for (int k = 0; k < 7; k++) Burst(s, k * 0.03f + (float)r.NextDouble() * 0.01f, 3600f + (float)r.NextDouble() * 900f, 6f, 0.006f, 0.6f, r);
                    Thump(s, 0.15f, 170f, 100f, 0.03f, 0.04f, 0.4f);
                    break;
                case "cylclose":
                    s = Buf(0.22f);
                    Burst(s, 0.0f, 2100f, 3f, 0.012f, 1.2f, r); Thump(s, 0.0f, 140f, 75f, 0.04f, 0.06f, 0.9f);
                    Chime(s, 0.0f, 2500f, 0.05f, new[] { 1f, 2.2f }, new[] { 0.3f, 0.12f }, 1f);
                    break;
                case "breakclose":
                    s = Buf(0.26f);
                    Burst(s, 0.0f, 1800f, 2.5f, 0.015f, 1.4f, r); Thump(s, 0.0f, 110f, 60f, 0.05f, 0.08f, 1.0f);
                    Chime(s, 0.0f, 2000f, 0.07f, new[] { 1f, 2.0f }, new[] { 0.3f, 0.14f }, 1f);
                    break;
                case "dry":
                    s = Buf(0.1f);
                    Burst(s, 0.0f, 3400f, 7f, 0.004f, 1.2f, r); Thump(s, 0.0f, 220f, 140f, 0.02f, 0.02f, 0.3f);
                    break;
                case "click":
                    s = Buf(0.08f);
                    Burst(s, 0.0f, 3000f, 6f, 0.005f, 1.0f, r);
                    break;
                case "holster":
                    s = Buf(0.22f);
                    Burst(s, 0.0f, 700f, 0.8f, 0.07f, 0.9f, r); Burst(s, 0.06f, 2200f, 3f, 0.01f, 0.5f, r);
                    break;
                default: // draw
                    s = Buf(0.28f);
                    Burst(s, 0.0f, 800f, 0.8f, 0.08f, 0.9f, r); Burst(s, 0.12f, 2400f, 4f, 0.01f, 0.8f, r); Thump(s, 0.12f, 150f, 90f, 0.04f, 0.04f, 0.4f);
                    break;
            }
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Bossolo che tocca terra: tintinnio acuto di ottone, o tonfo sordo di plastica per le cartucce.</summary>
        public static float[] CasingDrop(string kind, int seed)
        {
            var r = new System.Random(seed * 5 + kind.Length);
            var s = Buf(0.35f);
            if (kind == "shell")
            {
                Burst(s, 0.0f, 1100f, 1.5f, 0.02f, 1f, r); Thump(s, 0.0f, 260f, 150f, 0.03f, 0.03f, 0.6f);
                Burst(s, 0.09f, 1400f, 2f, 0.015f, 0.4f, r);
            }
            else
            {
                float f = kind == "pistol" ? 5200f : kind == "big" ? 3000f : 4200f;
                Chime(s, 0.0f, f, 0.07f, new[] { 1f, 2.76f, 5.4f }, new[] { 0.5f, 0.25f, 0.1f }, 1f);
                Chime(s, 0.08f, f * 1.1f, 0.05f, new[] { 1f, 2.76f }, new[] { 0.35f, 0.15f }, 1f);
                Burst(s, 0.0f, 4000f, 4f, 0.004f, 0.5f, r);
            }
            Normalize(s, 0.7f);
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

        /// <summary>Ronzio dell'occhio volante: un tono basso e viscido che respira (vibrato lento, tremolo a 3 Hz),
        /// sotto un fruscio umido a banda stretta che sale e scende e un sibilo acuto lontano. Loop di 2 s.</summary>
        public static float[] EyeHum()
        {
            var r = new System.Random(15);
            int n = Rate * 2, ext = Rate / 4;
            var s = new float[n + ext];
            var wet = Biquad.BandPass(900f, 2.2f);
            var hiss = Biquad.BandPass(3400f, 6f);
            var lp = Biquad.LowPass(420f, 0.8f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float vib = 1f + 0.035f * Mathf.Sin(TwoPi * 1.5f * t);
                float tone = Mathf.Sin(TwoPi * 52f * t * vib) + 0.5f * Mathf.Sin(TwoPi * 104.5f * t * vib) + 0.25f * Mathf.Sin(TwoPi * 157f * t);
                float trem = 0.65f + 0.35f * Mathf.Sin(TwoPi * 3f * t);
                float nz = N(r);
                float sweep = 0.5f + 0.5f * Mathf.Sin(TwoPi * 1f * t + 0.8f);
                s[i] = lp.Run(tone) * 0.30f * trem + wet.Run(nz) * (0.45f + 0.55f * sweep) + hiss.Run(nz) * 0.16f * (0.4f + 0.6f * Mathf.Sin(TwoPi * 0.5f * t + 2f));
            }
            var o = LoopSeamless(s, ext);
            Normalize(o, 0.55f);
            return o;
        }

        /// <summary>Sguardo fisso prima del colpo: un lamento acuto che sale (380 -> 920 Hz in mezzo secondo) con vibrato
        /// e un clic umido di apertura delle palpebre.</summary>
        public static float[] EyeStare()
        {
            var r = new System.Random(16);
            var s = Buf(0.7f);
            var bp = Biquad.BandPass(1200f, 1.5f);
            double ph = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float k = Mathf.Clamp01(t / 0.5f);
                float f = Mathf.Lerp(380f, 920f, k * k) * (1f + 0.02f * Mathf.Sin(TwoPi * 9f * t));
                ph += TwoPi * f / Rate;
                float env = Mathf.Clamp01(t / 0.06f) * Mathf.Exp(-Mathf.Max(0f, t - 0.5f) / 0.06f);
                s[i] = ((float)Math.Sin(ph) * 0.6f + bp.Run(N(r)) * 0.4f) * env;
            }
            Burst(s, 0f, 700f, 3f, 0.012f, 0.9f, r);
            Normalize(s, 0.7f);
            return s;
        }

        /// <summary>L'occhio muore: scoppio umido (fango che schizza), tonfo basso e un gorgoglio che cala.</summary>
        public static float[] EyeBurst()
        {
            var r = new System.Random(17);
            var s = Buf(1.3f);
            Thump(s, 0f, 90f, 40f, 0.12f, 0.16f, 0.45f);
            Burst(s, 0f, 500f, 0.9f, 0.05f, 2.6f, r);
            Burst(s, 0.01f, 1800f, 2f, 0.02f, 1.4f, r);
            Burst(s, 0.04f, 900f, 1.2f, 0.03f, 1.2f, r);
            var gl = Biquad.BandPass(320f, 3f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                if ((i & 63) == 0) gl.SetBandPass(Mathf.Lerp(700f, 150f, Mathf.Clamp01(t / 0.9f)), 3f, Rate);
                float env = Mathf.Clamp01((t - 0.03f) / 0.05f) * Mathf.Exp(-t / 0.35f);
                s[i] += gl.Run(N(r)) * env * (0.5f + 0.5f * Mathf.Sin(TwoPi * 17f * t));
            }
            Normalize(s, 0.9f);
            return s;
        }

        // ---------- chiesa e piani procedurali ----------
        /// <summary>Accensione di una candela: grattata di fiammifero, soffio della fiamma che prende, qualche scoppiettio.</summary>
        public static float[] Ignite()
        {
            var r = new System.Random(21);
            var s = Buf(1.1f);
            Burst(s, 0f, 3500f, 1.2f, 0.035f, 1.6f, r);                     // grattata
            var bp = Biquad.BandPass(600f, 0.7f);
            var lp = Biquad.LowPass(2400f, 0.7f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate - 0.09f;
                if (t < 0f) continue;
                float f = Mathf.Lerp(200f, 1500f, Mathf.Clamp01(t / 0.25f));
                if ((i & 31) == 0) bp.SetBandPass(f, 0.7f, Rate);
                float env = Mathf.Clamp01(t / 0.06f) * Mathf.Exp(-t / 0.28f);
                s[i] += lp.Run(bp.Run(N(r))) * env * 1.4f;
            }
            for (int k = 0; k < 9; k++) Burst(s, 0.25f + (float)r.NextDouble() * 0.7f, U(r, 1800f, 4500f), 3f, 0.004f, U(r, 0.2f, 0.7f), r);
            Normalize(s, 0.7f);
            return s;
        }

        /// <summary>Accordo d'organo basso e sospeso (re minore senza terza, con una seconda stonata che respira): loop di circa 14 s.</summary>
        public static float[] OrganDrone(out int rate)
        {
            rate = Rate;
            var r = new System.Random(22);
            float dur = 14f;
            var s = Buf(dur);
            float[] notes = { 73.42f, 110.0f, 146.83f, 220.0f, 164.81f * 0.5f, 233.1f };
            float[] amps = { 1.0f, 0.7f, 0.5f, 0.28f, 0.22f, 0.10f };
            var lp = Biquad.LowPass(900f, 0.6f);
            var br = Biquad.BandPass(1300f, 1.2f);
            double[] ph = new double[notes.Length * 3];
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float v = 0f;
                for (int n = 0; n < notes.Length; n++)
                {
                    // ogni canna: fondamentale, ottava e dodicesima, con una coppia leggermente scordata
                    float det = 1f + (n % 2 == 0 ? 0.0018f : -0.0023f);
                    float swell = 0.65f + 0.35f * Mathf.Sin(TwoPi * t / (9f + n * 1.7f) + n);
                    for (int h = 0; h < 3; h++)
                    {
                        float f = notes[n] * (h == 0 ? 1f : h == 1 ? 2f : 3f) * det;
                        ph[n * 3 + h] += TwoPi * f / Rate;
                        v += (float)Math.Sin(ph[n * 3 + h]) * amps[n] * swell / (1f + h * 1.6f);
                    }
                }
                float breath = br.Run(N(r)) * 0.03f * (0.5f + 0.5f * Mathf.Sin(TwoPi * t / 6.1f));
                float trem = 1f + 0.06f * Mathf.Sin(TwoPi * 5.3f * t);
                s[i] = lp.Run(v * trem + breath);
            }
            s = LoopSeamless(s, Rate * 2);
            Normalize(s, 0.5f);
            return s;
        }

        /// <summary>Rintocco di campana grande e lontana: parziali inarmoniche di bronzo, coda di diversi secondi.</summary>
        public static float[] BellToll()
        {
            var r = new System.Random(23);
            var s = Buf(9f);
            Chime(s, 0f, 196f, 4.0f, new[] { 0.5f, 1f, 1.19f, 1.5f, 2f, 2.51f, 3.0f, 4.07f }, new[] { 0.6f, 1f, 0.7f, 0.5f, 0.4f, 0.25f, 0.18f, 0.1f }, 1f);
            Thump(s, 0f, 196f, 96f, 0.02f, 0.5f, 0.35f);
            var hp = Biquad.HighPass(2500f);
            for (int i = 0; i < 400; i++) s[i] += hp.Run(N(r)) * Mathf.Exp(-i / 130f) * 0.5f;
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Discesa dell'ascensore: rombo di motore, ronzio di rete e qualche clangore dei cavi. Loop di 6 s.</summary>
        public static float[] LiftRide()
        {
            var r = new System.Random(24);
            var s = Buf(7f);
            var lp = Biquad.LowPass(120f, 0.8f);
            var lp2 = Biquad.LowPass(500f, 0.7f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float n = N(r);
                s[i] = lp.Run(n) * 3.2f + lp2.Run(n) * 0.12f + Mathf.Sin(TwoPi * 47f * t) * 0.10f + Mathf.Sin(TwoPi * 94f * t) * 0.05f
                       + Mathf.Sin(TwoPi * 31f * t) * 0.07f * (1f + 0.4f * Mathf.Sin(TwoPi * 0.4f * t));
            }
            for (int k = 0; k < 5; k++) Thump(s, 0.5f + k * 1.3f, U(r, 70f, 120f), 45f, 0.04f, 0.09f, U(r, 0.12f, 0.3f));
            s = LoopSeamless(s, Rate);
            Normalize(s, 0.5f);
            return s;
        }
    }
}
