using System;
using System.Collections.Generic;
using System.IO;
using PryingEyes.Pista;

/// <summary>The engine loops, measured: every voice renders to a finite, loud, balanced second that closes on itself without a click, most of its power sits on the firing pattern (an engine, not a hiss), and no
/// two engines have the same voice. --dump-engines DIR writes them as .wav files for a spectrogram.</summary>
static partial class Program
{
    /// <summary>The share of the power of a clip that sits on the lines of its firing pattern (multiples of patternHz: whole bins of a one-second clip). Noise spreads over every bin, so what sits on the lines is the engine.</summary>
    static double TonalShare(float[] s, int patternHz) { double[] lines; return TonalShare(s, patternHz, out lines); }

    static double TonalShare(float[] s, int patternHz, out double[] lines)
    {
        int n = s.Length;
        double total = 0; for (int i = 0; i < n; i++) total += (double)s[i] * s[i];
        double on = 0;
        lines = new double[n / 2 / patternHz];
        for (int k = patternHz; k < n / 2; k += patternHz)
        {
            double re = 0, im = 0, w = 2.0 * Math.PI * k / n;
            // a rotating phasor instead of a sine per sample
            double c = Math.Cos(w), sn = Math.Sin(w), pr = 1, pi = 0;
            for (int i = 0; i < n; i++)
            {
                re += s[i] * pr; im -= s[i] * pi;
                double nr = pr * c - pi * sn; pi = pr * sn + pi * c; pr = nr;
            }
            double pk = 2.0 * (re * re + im * im) / n;
            on += pk; if (k / patternHz - 1 < lines.Length) lines[k / patternHz - 1] = pk;
        }
        return on / Math.Max(1e-12, total);
    }

    static void WriteWav(string path, float[] samples, int repeats)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            int n = samples.Length * repeats;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(EngineSynth.Rate); w.Write(EngineSynth.Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
            for (int r = 0; r < repeats; r++) for (int i = 0; i < samples.Length; i++) w.Write((short)Math.Max(-32767, Math.Min(32767, Math.Round(samples[i] * 32767.0))));
        }
    }

    static void DumpEngines(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var v in EngineSynth.All()) WriteWav(Path.Combine(dir, "engine_" + v.name.Replace(' ', '_') + ".wav"), EngineSynth.Render(v), 3);
        Console.WriteLine("engines written to " + dir);
    }

    static void SoundChecks()
    {
        var voices = EngineSynth.All();
        var bright = new float[voices.Length]; var centroid = new double[voices.Length];
        bool finite = true, level = true, closes = true, smooth = true, tonal = true, rich = true;
        string detail = "";
        for (int a = 0; a < voices.Length; a++)
        {
            var v = voices[a];
            var s = EngineSynth.Render(v);
            double sum = 0, sq = 0, dsq = 0; float peak = 0f; float maxStep = 0f; bool ok1 = s.Length == EngineSynth.Rate;
            for (int i = 0; i < s.Length; i++)
            {
                if (float.IsNaN(s[i]) || float.IsInfinity(s[i])) { ok1 = false; break; }
                sum += s[i]; sq += (double)s[i] * s[i]; peak = Math.Max(peak, Math.Abs(s[i]));
                if (i > 0) { float st = Math.Abs(s[i] - s[i - 1]); dsq += (double)st * st; maxStep = Math.Max(maxStep, st); }
            }
            finite &= ok1;
            if (!ok1) { detail += " [" + v.name + " NOT FINITE]"; continue; }
            float rms = (float)Math.Sqrt(sq / s.Length), rmsDiff = (float)Math.Sqrt(dsq / s.Length), dc = (float)(sum / s.Length);
            bright[a] = rmsDiff / rms;
            float seam = Math.Abs(s[0] - s[s.Length - 1]);
            double[] lines; double share = TonalShare(s, (int)v.patternHz, out lines);
            double strongest = 0; foreach (double p in lines) strongest = Math.Max(strongest, p);
            double sumLines = 0; foreach (double p in lines) sumLines += p;
            double purity = strongest / Math.Max(1e-12, sumLines);                                      // 1 would be a single sine
            int harmonics = 0; foreach (double p in lines) if (p > strongest * 1e-3) harmonics++;      // within 30 dB of the strongest line of the pattern
            rich &= purity < 0.6 && harmonics >= 5;
            if (Environment.GetEnvironmentVariable("SOUND_LINES") != null)       // SOUND_LINES=1 shows the signature of the firing order: the missing fourth line of a 270/450 twin, only the multiples of three for an even triple
            {
                string ls = ""; for (int q = 0; q < Math.Min(14, lines.Length); q++) ls += " " + (10.0 * Math.Log10(Math.Max(1e-12, lines[q] / strongest))).ToString("F0");
                Console.WriteLine("lines of " + v.name + " (dB below the strongest, every " + v.patternHz + " Hz):" + ls);
            }
            centroid[a] = EngineSynth.Centroid(s);
            level &= rms > 0.08f && rms < 0.5f && Math.Abs(peak - 0.72f) < 0.005f && Math.Abs(dc) < 0.002f;
            closes &= seam < 3f * rmsDiff + 0.005f;
            smooth &= maxStep < 14f * rmsDiff;
            tonal &= share > 0.40 && share < 0.92;
            detail += " [" + v.name + ": rms " + rms.ToString("F3") + ", seam " + seam.ToString("F3") + " (step " + rmsDiff.ToString("F3") + ", worst " + maxStep.ToString("F3") + "), centre " + centroid[a].ToString("F0") + " Hz, on the firing lines " + (share * 100).ToString("F0") + " %, strongest line " + (purity * 100).ToString("F0") + " % of the note, " + harmonics + " lines within 30 dB]";
        }
        Check("SOUND_ENGINES_RENDER_FINITE_LOUD_BALANCED", finite && level, voices.Length + " voices:" + detail, "one second each, peak 0.72, rms between 0.08 and 0.5, no direct component");
        Check("SOUND_ENGINE_LOOPS_CLOSE_WITHOUT_A_CLICK", closes, "step across the seam against the step of the waveform", "less than three typical steps");
        Check("SOUND_ENGINES_HAVE_NO_CLICK_AT_THE_FIRINGS", smooth, "the worst step of the waveform against the typical one", "less than fourteen times: a ring cut at the next firing would click");
        Check("SOUND_ENGINE_POWER_IS_NOTE_WITH_A_TEXTURE", tonal, "share of the power on the lines of the pattern" + detail, "between 40 and 92 %: an engine, neither a hiss nor an organ pipe");
        Check("SOUND_ENGINES_ARE_RICH_IN_HARMONICS", rich, "how the note spreads over the lines of the firing pattern" + detail, "no line carries 60 % of the note and at least five lines are within 30 dB: a bark, not a single organ pipe");
        bool distinct = true;
        for (int i = 0; i < voices.Length; i++) for (int j = i + 1; j < voices.Length; j++) distinct &= Math.Abs(centroid[i] - centroid[j]) / Math.Max(centroid[i], centroid[j]) > 0.15;
        Check("SOUND_EVERY_ENGINE_HAS_A_VOICE_OF_ITS_OWN", distinct, "centre of the spectrum (Hz) " + string.Join(", ", Array.ConvertAll(centroid, c => c.ToString("F0"))), "no two within 15 % of each other");
        Check("SOUND_THE_TWIN_IS_THE_DARKEST_AND_THE_TWO_STROKES_THE_BRIGHTEST", centroid[0] < centroid[1] && centroid[1] < centroid[2] && centroid[2] < centroid[3], "twin " + centroid[0].ToString("F0") + " < triple " + centroid[1].ToString("F0") + " < two-stroke single " + centroid[2].ToString("F0") + " < V4 " + centroid[3].ToString("F0"), "dark to bright, as the real engines are");
        // the same voice and seed give the same loop: the clip is the same on every machine
        var s1 = EngineSynth.Render(EngineSynth.Twin(47)); var s2 = EngineSynth.Render(EngineSynth.Twin(47)); var s3 = EngineSynth.Render(EngineSynth.Twin(48));
        bool same = true, other = false; for (int i = 0; i < s1.Length; i++) { same &= s1[i] == s2[i]; other |= s1[i] != s3[i]; }
        Check("SOUND_ENGINE_LOOPS_ARE_REPEATABLE", same && other, "same seed same clip: " + same + ", other seed other noise: " + other, "deterministic");
    }
}
