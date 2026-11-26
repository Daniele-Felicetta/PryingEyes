using PryingEyes.Pista;
using UnityEngine;
using Production.Racing;

namespace PryingEyes
{
    /// <summary>The sounds of the track mode, made by code like all the others (no audio files): the loop of an inline-four sport engine, the squeal of a sliding tire, the rumble of kerbs and gravel, the rush of
    /// the wind, the beeps of the start lights.</summary>
    public static partial class SoundGen
    {
        /// <summary>A one-second loop of an inline-four: every frequency is a whole number of hertz, so it closes on itself. The firing frequency is 160 Hz (4800 rpm at pitch 1: the game plays it at
        /// pitch rpm / 4800), with the harmonics of the exhaust note, a growl of intake noise that beats at the firing rate and a brighter hiss of valves.</summary>
        public static float[] SportEngine(int seed)
        {
            const float f0 = 160f;
            var r = new System.Random(seed);
            int loop = Mathf.RoundToInt(Rate), seam = 900;
            var s = new float[loop + seam];
            float[] amp = { 1.0f, 0.60f, 0.72f, 0.40f, 0.45f, 0.26f, 0.28f, 0.15f, 0.16f, 0.09f, 0.08f, 0.05f };
            float[] ph = { 0.0f, 1.3f, 2.6f, 0.4f, 2.1f, 3.0f, 0.9f, 1.8f, 2.7f, 0.6f, 1.5f, 2.4f };
            var growl = Biquad.BandPass(520f, 1.1f);
            var hiss = Biquad.BandPass(3100f, 1.4f);
            var rumble = Biquad.LowPass(240f, 0.8f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float u = t * f0 - Mathf.Floor(t * f0);
                float pulse = Mathf.Exp(-u * 4.5f) * Mathf.Clamp01(u / 0.03f);
                float body = 0.22f * Mathf.Sin(TwoPi * 80f * t + 0.7f);                      // the half order: the roughness of a four that is not perfectly even
                for (int k = 0; k < amp.Length; k++) body += amp[k] * Mathf.Sin(TwoPi * f0 * (k + 1) * t + ph[k]);
                float noise = N(r);
                float v = body * (0.28f + 0.72f * pulse) * 0.16f + growl.Run(noise) * 0.9f * pulse + hiss.Run(noise) * 0.07f * (0.4f + 0.6f * pulse) + rumble.Run(noise) * 0.5f * pulse;
                s[i] = v;
            }
            SoftClip(s, 1.5f);
            var o = LoopSeamless(s, seam);
            double mean = 0; for (int i = 0; i < o.Length; i++) mean += o[i];
            float dc = (float)(mean / o.Length);
            for (int i = 0; i < o.Length; i++) o[i] -= dc;
            Normalize(o, 0.72f);
            return o;
        }

        /// <summary>The engines that are not a four are made by EngineSynth (pure code, so that the lab measures what the game plays): a train of firings that rings the resonances of the exhaust.
        /// A 90-degree V-twin: two firings 270 and 450 degrees apart, a lopsided beat with a deep ring.</summary>
        public static float[] TwinEngine(int seed) { return EngineSynth.Render(EngineSynth.Twin(seed)); }

        /// <summary>An inline three: three even firings every two turns, a rasp between the four's scream and the twin's throb.</summary>
        public static float[] TripleEngine(int seed) { return EngineSynth.Render(EngineSynth.Triple(seed)); }

        /// <summary>A two-stroke single: a firing every turn, sharp and dry, with the ring of the expansion chamber high above the beat.</summary>
        public static float[] TwoStrokeSingle(int seed) { return EngineSynth.Render(EngineSynth.TwoStrokeSingle(seed)); }

        /// <summary>A two-stroke V4: four firings a turn, so close together that they blur into a buzz, with the same ring on top.</summary>
        public static float[] TwoStrokeV4(int seed) { return EngineSynth.Render(EngineSynth.TwoStrokeV4(seed)); }

        /// <summary>A pop in the exhaust as the throttle closes at high revs: a crack and a low thump, a fifth of a second.</summary>
        public static float[] Pop(int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(0.2f);
            var crack = Biquad.BandPass(1100f, 0.9f); var body = Biquad.LowPass(420f, 0.8f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float n = N(r);
                float env = Mathf.Exp(-t * 26f) * Mathf.Clamp01(t / 0.002f);
                s[i] = (crack.Run(n) * 1.0f + body.Run(n) * 0.9f + 0.5f * Mathf.Sin(TwoPi * 85f * t) * Mathf.Exp(-t * 30f)) * env;
            }
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>The squeal of a tire at its limit: two bands of noise that waver, in a one-second loop.</summary>
        public static float[] TyreSqueal(int seed)
        {
            var r = new System.Random(seed);
            int loop = Mathf.RoundToInt(Rate), seam = 1200;
            var s = new float[loop + seam];
            var a = Biquad.BandPass(1850f, 5f); var b = Biquad.BandPass(2950f, 4f); var c = Biquad.BandPass(900f, 2f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float wob = 0.65f + 0.35f * Mathf.Sin(TwoPi * 13f * t + 0.4f) * Mathf.Sin(TwoPi * 3f * t);
                float n = N(r);
                s[i] = (a.Run(n) * 1.0f + b.Run(n) * 0.7f + c.Run(n) * 0.35f) * wob;
            }
            var o = LoopSeamless(s, seam);
            Normalize(o, 0.6f);
            return o;
        }

        /// <summary>Rubber on a rough surface (kerbs and gravel): a low buzz and a crunch, in a one-second loop. 'kerb' pulses at 56 Hz, gravel is a bed of tiny clicks.</summary>
        public static float[] Rough(bool kerb, int seed)
        {
            var r = new System.Random(seed);
            int loop = Mathf.RoundToInt(Rate), seam = 1200;
            var s = new float[loop + seam];
            var lo = Biquad.LowPass(kerb ? 260f : 700f, 0.8f); var mid = Biquad.BandPass(kerb ? 900f : 1900f, 0.9f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float n = N(r);
                if (kerb)
                {
                    float u = t * 56f - Mathf.Floor(t * 56f);
                    float env = Mathf.Exp(-u * 6f);
                    s[i] = lo.Run(n) * (0.4f + 0.9f * env) + mid.Run(n) * 0.3f * env + 0.3f * Mathf.Sin(TwoPi * 56f * t) * env;
                }
                else
                {
                    float click = r.NextDouble() < 0.012 ? N(r) * 2.2f : 0f;
                    s[i] = lo.Run(n) * 0.5f + mid.Run(n) * 0.5f + mid.Run(click) * 0.8f;
                }
            }
            var o = LoopSeamless(s, seam);
            Normalize(o, 0.6f);
            return o;
        }

        /// <summary>The rush of the air past the helmet: low-passed noise with slow swells, in a two-second loop.</summary>
        public static float[] WindRush(int seed)
        {
            var r = new System.Random(seed);
            int loop = Mathf.RoundToInt(Rate * 2f), seam = 2000;
            var s = new float[loop + seam];
            var a = Biquad.LowPass(1100f, 0.7f); var b = Biquad.BandPass(380f, 0.8f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float swell = 0.75f + 0.25f * Mathf.Sin(TwoPi * 0.5f * t) * Mathf.Sin(TwoPi * 1.5f * t + 1f);
                float n = N(r);
                s[i] = (a.Run(n) * 0.8f + b.Run(n) * 0.6f) * swell;
            }
            var o = LoopSeamless(s, seam);
            Normalize(o, 0.6f);
            return o;
        }

        /// <summary>A short clean beep (the lights of the start).</summary>
        public static float[] Beep(float freq, float seconds)
        {
            var s = Buf(seconds);
            int n = s.Length, ramp = Mathf.Max(1, (int)(0.006f * Rate));
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Min(1f, Mathf.Min(i, n - 1 - i) / (float)ramp);
                s[i] = (Mathf.Sin(TwoPi * freq * t) + 0.25f * Mathf.Sin(TwoPi * freq * 2f * t)) * env * 0.5f;
            }
            return s;
        }

        /// <summary>A whoosh for a bike passing close: noise with a rising and falling band, one second.</summary>
        public static float[] Pass(int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(1.0f);
            var hi = Biquad.BandPass(1800f, 0.9f); var lo = Biquad.BandPass(500f, 0.9f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / s.Length;
                float env = Mathf.Sin(Mathf.PI * t); env *= env;
                float n = N(r);
                s[i] = (hi.Run(n) * (1f - t) + lo.Run(n) * t) * env;
            }
            Normalize(s, 0.6f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip sportEngine, tyreSqueal, kerbRough, gravelRough, windRush, beepLow, beepHigh, twinEngine, tripleEngine, twoStrokeSingle, twoStrokeV4, pop;

        public static AudioClip SportEngine => One(ref sportEngine, "pistaMotore", () => SoundGen.SportEngine(41));
        public static AudioClip TwinEngine => One(ref twinEngine, "pistaMotoreBicilindrico", () => SoundGen.TwinEngine(47));
        public static AudioClip TripleEngine => One(ref tripleEngine, "pistaMotoreTre", () => SoundGen.TripleEngine(48));
        public static AudioClip TwoStrokeSingle => One(ref twoStrokeSingle, "pistaMotoreDueTempi", () => SoundGen.TwoStrokeSingle(49));
        public static AudioClip TwoStrokeV4 => One(ref twoStrokeV4, "pistaMotoreDueTempiV4", () => SoundGen.TwoStrokeV4(50));
        public static AudioClip Pop => One(ref pop, "pistaScoppio", () => SoundGen.Pop(51));

        /// <summary>The loop of an engine of this kind.</summary>
        public static AudioClip ForEngine(EngineKind kind)
        {
            switch (kind)
            {
                case EngineKind.V2: return TwinEngine;
                case EngineKind.Triple: return TripleEngine;
                case EngineKind.TwoStroke125: return TwoStrokeSingle;
                case EngineKind.TwoStroke500: return TwoStrokeV4;
                default: return SportEngine;
            }
        }
        public static AudioClip TyreSqueal => One(ref tyreSqueal, "pistaGomme", () => SoundGen.TyreSqueal(43));
        public static AudioClip KerbRough => One(ref kerbRough, "pistaCordoli", () => SoundGen.Rough(true, 44));
        public static AudioClip GravelRough => One(ref gravelRough, "pistaGhiaia", () => SoundGen.Rough(false, 45));
        public static AudioClip WindRush => One(ref windRush, "pistaVento", () => SoundGen.WindRush(46));
        public static AudioClip BeepLow => One(ref beepLow, "pistaBip", () => SoundGen.Beep(820f, 0.22f));
        public static AudioClip BeepHigh => One(ref beepHigh, "pistaVia", () => SoundGen.Beep(1640f, 0.60f));
    }
}
