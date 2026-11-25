using System;

namespace PryingEyes.Pista
{
    /// <summary>The loops of the engines that are not an inline four, made of arithmetic only (no Unity), so that the lab can measure what the game plays. An engine is a train of firings that excites the
    /// resonances of its exhaust: a firing is a short smooth pulse (the pressure wave that leaves the cylinder), a resonance is a two-pole ringing that dies away on its own (so a ring runs on across the
    /// next firing instead of being cut by it: a cut ring clicks at every firing), and three noises (a growl of intake, a hiss of valves or of the exhaust chamber, a rumble) swell with the firings.
    /// The loop is the sound at 4800 rpm (the game plays it at pitch rpm / 4800): <see cref="Voice.patternHz"/> is how often the firing pattern repeats at that speed, a whole number so that the loop closes on itself.</summary>
    public static class EngineSynth
    {
        public const int Rate = 44100;

        /// <summary>One kind of engine: when it fires, how hard, what its exhaust rings at, what noise rides on it.</summary>
        public sealed class Voice
        {
            public string name;
            public int seed;
            public float patternHz;                      // how often the pattern of firings repeats at 4800 rpm (one pattern = one cycle of the engine, or one turn for a two-stroke)
            public float[] phases, gains;                // where in the pattern the firings are (0 to 1) and how hard each one is
            public float pulseMs;                        // the width of a firing pulse: the narrower, the more of the high resonances it excites
            public float[] res, resGain, resDecay;       // the resonances of the exhaust: hertz, level, decay per second
            public float envDecay;                       // how fast the noises die away after a firing, in pattern lengths
            public float growlHz, growlGain, hissHz, hissGain, rumbleHz, rumbleGain;
            public float noiseShare = 0.3f;              // the share of the power that is noise (growl, hiss, rumble) and not ringing: an engine is mostly note, with a texture on top
            public float drive;                          // how hard the exhaust note is pushed into the soft clipper
        }

        /// <summary>A 90-degree V-twin: two firings 270 and 450 degrees apart, a lopsided beat with a deep ring and hardly any hiss.</summary>
        public static Voice Twin(int seed)
        {
            return new Voice
            {
                name = "twin", seed = seed, patternHz = 40f, phases = new[] { 0f, 0.375f }, gains = new[] { 1f, 0.85f }, pulseMs = 0.30f,
                res = new[] { 90f, 200f, 330f, 520f, 800f, 1200f }, resGain = new[] { 1f, 0.9f, 0.8f, 0.6f, 0.45f, 0.3f }, resDecay = new[] { 180f, 300f, 450f, 600f, 800f, 1100f }, envDecay = 9f,
                growlHz = 330f, growlGain = 0.8f, hissHz = 2200f, hissGain = 0.04f, rumbleHz = 160f, rumbleGain = 0.7f, noiseShare = 0.25f, drive = 1.7f,
            };
        }

        /// <summary>An inline three: three even firings every two turns, a rasp between the four's scream and the twin's throb.</summary>
        public static Voice Triple(int seed)
        {
            return new Voice
            {
                name = "triple", seed = seed, patternHz = 40f, phases = new[] { 0f, 1f / 3f, 2f / 3f }, gains = new[] { 1f, 0.95f, 0.9f }, pulseMs = 0.25f,
                res = new[] { 130f, 260f, 400f, 600f, 900f, 1300f, 1800f }, resGain = new[] { 0.7f, 1f, 0.9f, 0.8f, 0.6f, 0.45f, 0.3f }, resDecay = new[] { 250f, 350f, 450f, 600f, 800f, 1000f, 1300f }, envDecay = 11f,
                growlHz = 480f, growlGain = 0.9f, hissHz = 2900f, hissGain = 0.07f, rumbleHz = 220f, rumbleGain = 0.5f, noiseShare = 0.30f, drive = 1.5f,
            };
        }

        /// <summary>A two-stroke single: a firing every turn, sharp and dry, with the ring of the expansion chamber high above the beat.</summary>
        public static Voice TwoStrokeSingle(int seed)
        {
            return new Voice
            {
                name = "two-stroke single", seed = seed, patternHz = 80f, phases = new[] { 0f }, gains = new[] { 1f }, pulseMs = 0.12f,
                res = new[] { 170f, 340f, 700f, 1400f, 1850f, 2600f, 3400f }, resGain = new[] { 0.6f, 0.8f, 0.8f, 0.7f, 1f, 0.9f, 0.6f }, resDecay = new[] { 200f, 250f, 350f, 500f, 450f, 700f, 900f }, envDecay = 14f,
                growlHz = 900f, growlGain = 1.0f, hissHz = 2500f, hissGain = 0.16f, rumbleHz = 260f, rumbleGain = 0.3f, noiseShare = 0.40f, drive = 1.8f,
            };
        }

        /// <summary>A two-stroke V4: four firings a turn, so close together that they blur into a buzz, with the same ring on top.</summary>
        public static Voice TwoStrokeV4(int seed)
        {
            return new Voice
            {
                name = "two-stroke V4", seed = seed, patternHz = 80f, phases = new[] { 0f, 0.25f, 0.5f, 0.75f }, gains = new[] { 1f, 0.9f, 0.95f, 0.85f }, pulseMs = 0.10f,
                res = new[] { 260f, 520f, 1100f, 1800f, 2300f, 3300f, 4300f }, resGain = new[] { 0.6f, 0.8f, 0.9f, 0.8f, 1f, 0.9f, 0.6f }, resDecay = new[] { 300f, 350f, 500f, 700f, 600f, 900f, 1200f }, envDecay = 22f,
                growlHz = 1300f, growlGain = 0.9f, hissHz = 3300f, hissGain = 0.18f, rumbleHz = 300f, rumbleGain = 0.2f, noiseShare = 0.45f, drive = 1.9f,
            };
        }

        /// <summary>The four voices with the seeds the game uses.</summary>
        public static Voice[] All()
        {
            return new[] { Twin(47), Triple(48), TwoStrokeSingle(49), TwoStrokeV4(50) };
        }

        /// <summary>One second of the engine at 4800 rpm, ready to loop: normalised to a peak of 0.72, without a direct component, with no step at the seam.</summary>
        public static float[] Render(Voice v)
        {
            const int seam = 900;
            int warm = Rate / 2, loop = Rate;                      // half a second of run-up, so that every ring is at its steady state when the clip starts
            var rng = new Random(v.seed);
            int nr = v.res.Length;
            var a1 = new double[nr]; var a2 = new double[nr]; var b = new double[nr]; var y1 = new double[nr]; var y2 = new double[nr];
            for (int k = 0; k < nr; k++)
            {
                double th = 2.0 * Math.PI * v.res[k] / Rate, r = Math.Exp(-v.resDecay[k] / Rate);
                a1[k] = 2.0 * r * Math.Cos(th); a2[k] = -r * r; b[k] = 2.0 * (1.0 - r) * Math.Sin(th) * v.resGain[k];     // the response at the centre of the resonance is resGain, however wide or narrow the resonance is
            }
            var growl = Filter.BandPass(v.growlHz, 1.1); var hiss = Filter.BandPass(v.hissHz, 1.4); var rumble = Filter.LowPass(v.rumbleHz, 0.8);
            double tau = v.pulseMs * 1e-3, pulseNorm = 1.0 / (Math.E * tau * Rate);                       // a pulse of area 1, in samples
            var ring = new double[loop + seam]; var hush = new double[loop + seam];
            for (int i = -warm; i < loop + seam; i++)
            {
                double u = (double)i / Rate * v.patternHz; u -= Math.Floor(u);                           // the pattern is locked to the clock of the clip, so that it repeats exactly every second
                double pulse = 0, burst = 0;
                for (int j = 0; j < v.phases.Length; j++)
                {
                    double x = u - v.phases[j]; if (x < 0.0) x += 1.0;
                    double dt = x / v.patternHz;
                    if (dt < 14.0 * tau) pulse += v.gains[j] * pulseNorm * (dt / tau) * Math.Exp(1.0 - dt / tau);
                    burst += v.gains[j] * Math.Exp(-x * v.envDecay) * Math.Min(1.0, x / 0.015);
                }
                double tone = 0;
                for (int k = 0; k < nr; k++)
                {
                    double y = a1[k] * y1[k] + a2[k] * y2[k] + b[k] * pulse;
                    y2[k] = y1[k]; y1[k] = y; tone += y;
                }
                double noise = rng.NextDouble() * 2.0 - 1.0;
                double texture = growl.Run(noise) * v.growlGain * burst + hiss.Run(noise) * v.hissGain * (0.35 + 0.65 * burst) + rumble.Run(noise) * v.rumbleGain * burst;
                if (i >= 0) { ring[i] = tone; hush[i] = texture; }
            }
            // the note and the texture are scaled apart and mixed by the share the voice asks for, so that the balance does not depend on how the numbers of a voice happen to add up
            double rt = 0, rn = 0; for (int i = 0; i < ring.Length; i++) { rt += ring[i] * ring[i]; rn += hush[i] * hush[i]; }
            double gt = Math.Sqrt(1.0 - v.noiseShare) / Math.Max(1e-12, Math.Sqrt(rt / ring.Length)), gn = Math.Sqrt(v.noiseShare) / Math.Max(1e-12, Math.Sqrt(rn / hush.Length));
            var s = new double[loop + seam];
            for (int i = 0; i < s.Length; i++) s[i] = ring[i] * gt + hush[i] * gn;
            double k0 = 0.4;                                                                              // the clipper sees a signal of a known size (rms 0.4), whatever the voice
            double norm = 1.0 / Math.Tanh(v.drive);
            for (int i = 0; i < s.Length; i++) s[i] = Math.Tanh(s[i] * k0 * v.drive) * norm;
            var o = new float[loop];
            for (int i = 0; i < loop; i++)
            {
                if (i < seam) { double w = (double)i / seam; o[i] = (float)(s[i] * w + s[loop + i] * (1.0 - w)); }     // the start dissolves into what comes after the end: the loop does not click
                else o[i] = (float)s[i];
            }
            double mean = 0; for (int i = 0; i < loop; i++) mean += o[i];
            float dc = (float)(mean / loop);
            float peak = 0f;
            for (int i = 0; i < loop; i++) { o[i] -= dc; peak = Math.Max(peak, Math.Abs(o[i])); }
            if (peak > 1e-6f) { float g = 0.72f / peak; for (int i = 0; i < loop; i++) o[i] *= g; }
            return o;
        }

    /// <summary>The centre of gravity of the spectrum of a clip, in hertz (a radix-2 transform of the clip padded to 65536 samples): the dark engines sit low, the screaming ones high. The lab and the game's own test both measure with it.</summary>
    public static double Centroid(float[] s)
    {
        int n = 65536;
        var re = new double[n]; var im = new double[n];
        for (int i = 0; i < s.Length; i++) re[i] = s[i] * (0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (s.Length - 1)));
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2.0 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                    double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
        double num = 0, den = 0;
        for (int k = 1; k < n / 2; k++) { double p = re[k] * re[k] + im[k] * im[k]; num += p * k * (double)EngineSynth.Rate / n; den += p; }
        return num / Math.Max(1e-12, den);
    }

        /// <summary>A second-order filter (the audio-EQ cookbook ones), in double precision.</summary>
        sealed class Filter
        {
            double b0, b1, b2, a1, a2, x1, x2, y1, y2;

            static void Coefs(double f, double q, out double c, out double alpha)
            {
                double w = 2.0 * Math.PI * Math.Min(Math.Max(f, 10.0), Rate * 0.45) / Rate;
                c = Math.Cos(w); alpha = Math.Sin(w) / (2.0 * Math.Max(q, 0.05));
            }

            public static Filter BandPass(double f, double q)
            {
                double c, al; Coefs(f, q, out c, out al);
                double a0 = 1.0 + al;
                return new Filter { b0 = al / a0, b1 = 0.0, b2 = -al / a0, a1 = -2.0 * c / a0, a2 = (1.0 - al) / a0 };
            }

            public static Filter LowPass(double f, double q)
            {
                double c, al; Coefs(f, q, out c, out al);
                double a0 = 1.0 + al;
                return new Filter { b0 = (1.0 - c) * 0.5 / a0, b1 = (1.0 - c) / a0, b2 = (1.0 - c) * 0.5 / a0, a1 = -2.0 * c / a0, a2 = (1.0 - al) / a0 };
            }

            public double Run(double x)
            {
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }
    }
}
