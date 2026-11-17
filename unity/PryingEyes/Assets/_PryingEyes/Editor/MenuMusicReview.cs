using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PryingEyes.EditorTools
{
    /// <summary>Genera il tema del menu e i toni dell'interfaccia fuori dal gioco, ne controlla i numeri (durata, finitezza, picco e volume, stereo,
    /// richiusura del giro, nessun silenzio, la melodia contro gli accordi) e li scrive in WAV, per misurarli fuori (unity_logs/menu-music).
    /// Non si puo' ascoltare da qui: l'orecchio vero e' dell'utente, questi sono i numeri che dicono che nulla e' rotto.</summary>
    public static class MenuMusicReview
    {
        static int failures;
        static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            UnityEngine.Debug.Log("MENU MUSIC " + (ok ? "PASS " : "FAIL ") + label);
        }

        public static void Run()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../unity_logs/menu-music"));
            Directory.CreateDirectory(output);
            try { RunChecks(output); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); failures++; }
            File.WriteAllText(Path.Combine(output, "result.txt"), failures == 0 ? "PASS" : "FAIL " + failures);
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void RunChecks(string output)
        {
            const int rate = SoundGen.MenuRate;
            int frames = (int)(SoundGen.MenuLoop * rate);
            var timer = Stopwatch.StartNew();
            float[] theme = SoundGen.MenuTheme();
            double seconds = timer.Elapsed.TotalSeconds;
            Check("the theme takes less than 10 s to compute (" + seconds.ToString("0.0") + " s)", seconds < 10.0);
            Check("96 s of stereo at 22,05 kHz", theme.Length == 2 * frames);

            double sum = 0, left = 0, right = 0, cross = 0;
            float peak = 0f;
            bool finite = true;
            for (int i = 0; i < frames; i++)
            {
                float l = theme[2 * i], r = theme[2 * i + 1];
                if (float.IsNaN(l) || float.IsInfinity(l) || float.IsNaN(r) || float.IsInfinity(r)) { finite = false; break; }
                peak = Mathf.Max(peak, Mathf.Abs(l), Mathf.Abs(r));
                left += l * l; right += r * r; cross += l * r;
            }
            Check("every sample is finite", finite);
            sum = left + right;
            double rmsDb = 10.0 * Math.Log10(sum / (2.0 * frames));
            double balance = 10.0 * Math.Log10(left / right);
            double corr = cross / Math.Sqrt(left * right);
            Check("peak is 0.85 (" + peak.ToString("0.000") + ")", Mathf.Abs(peak - 0.85f) < 0.002f);
            // il campione e' pieno (picco 0,85, poca differenza tra picco e media); e' MenuMusic.Level che lo porta a un volume da sottofondo
            double playDb = rmsDb + 20.0 * Math.Log10(MenuMusic.Level);
            Check("clip RMS " + rmsDb.ToString("0.0") + " dBFS is a full-scale master (-14 to -6)", rmsDb > -14.0 && rmsDb < -6.0);
            Check("at MenuMusic.Level the RMS is " + playDb.ToString("0.0") + " dBFS (-32 to -20: a background level)", playDb > -32.0 && playDb < -20.0);
            Check("left and right levels within 3 dB (" + balance.ToString("0.00") + ")", Math.Abs(balance) < 3.0);
            Check("stereo, not dual mono: L/R correlation between 0.2 and 0.97 (" + corr.ToString("0.000") + ")", corr > 0.2 && corr < 0.97);

            // nessun secondo di silenzio: l'organo tiene sempre
            double quietest = double.MaxValue;
            for (int s = 0; s < (int)SoundGen.MenuLoop; s++)
            {
                double e = 0;
                for (int i = s * rate; i < (s + 1) * rate; i++) e += theme[2 * i] * theme[2 * i] + theme[2 * i + 1] * theme[2 * i + 1];
                quietest = Math.Min(quietest, 10.0 * Math.Log10(e / (2.0 * rate) + 1e-12));
            }
            Check("no second quieter than -50 dBFS (the quietest is " + quietest.ToString("0.0") + ")", quietest > -50.0);

            // la richiusura: il salto tra l'ultimo campione e il primo e' dello stesso ordine degli altri, e il livello e' lo stesso a cavallo
            foreach (int channel in new[] { 0, 1 })
            {
                var diffs = new List<float>(frames);
                for (int i = 0; i < frames - 1; i++) diffs.Add(Mathf.Abs(theme[2 * (i + 1) + channel] - theme[2 * i + channel]));
                diffs.Sort();
                float p99 = diffs[(int)(diffs.Count * 0.99)];
                float jump = Mathf.Abs(theme[channel] - theme[2 * (frames - 1) + channel]);
                Check("loop seam channel " + channel + ": jump " + jump.ToString("0.00000") + " vs 99th percentile step " + p99.ToString("0.00000"), jump <= 3f * p99 + 1e-6f);
            }
            double tailE = 0, headE = 0;
            int window = rate / 4;
            for (int i = 0; i < window; i++)
            {
                int a = frames - window + i;
                tailE += theme[2 * a] * theme[2 * a]; headE += theme[2 * i] * theme[2 * i];
            }
            double seam = 10.0 * Math.Log10((tailE + 1e-12) / (headE + 1e-12));
            Check("last and first quarter second have similar level (" + seam.ToString("0.0") + " dB)", Math.Abs(seam) < 10.0);

            CheckScore();
            CheckClip(frames);
            Check("MenuMusic volume maths", Mathf.Approximately(MenuMusic.TargetVolume(1f, 1f, 1f), MenuMusic.Level)
                && Mathf.Approximately(MenuMusic.TargetVolume(0.5f, 1f, 1f), MenuMusic.Level * 0.25f) && MenuMusic.TargetVolume(1f, 0f, 1f) == 0f && MenuMusic.TargetVolume(0f, 1f, 1f) == 0f);

            WriteWav(Path.Combine(output, "menu_theme.wav"), theme, 2, rate);
            var tones = new Dictionary<string, float[]> { { "tick", SoundGen.UiTick() }, { "step", SoundGen.UiStep() }, { "confirm", SoundGen.UiConfirm() }, { "back", SoundGen.UiBack() } };
            foreach (var pair in tones)
            {
                float p = 0f; bool ok = true;
                foreach (float v in pair.Value) { if (float.IsNaN(v) || float.IsInfinity(v)) ok = false; p = Mathf.Max(p, Mathf.Abs(v)); }
                double length = pair.Value.Length / (double)SoundGen.Rate;
                Check("ui tone " + pair.Key + ": finite, peak " + p.ToString("0.00") + " (0.3 to 0.7), " + length.ToString("0.00") + " s (0.05 to 1.0)",
                    ok && p > 0.3f && p < 0.7f && length > 0.05 && length < 1.0);
                WriteWav(Path.Combine(output, "ui_" + pair.Key + ".wav"), pair.Value, 1, SoundGen.Rate);
            }
        }

        /// <summary>La partitura: ogni battuta e' di tre battiti, le note stanno nel registro del carillon e quasi tutte le note sul primo battito
        /// sono dell'accordo (le eccezioni sono quelle volute: le note che si stonano nel terzo giro).</summary>
        static void CheckScore()
        {
            var notes = SoundGen.MenuMelodyNotes();
            var lengths = new float[SoundGen.MenuBars];
            int outside = 0, strongOutside = 0, strong = 0;
            foreach (var note in notes)
            {
                lengths[note.bar] += note.len;
                if (note.midi < 62 || note.midi > 88) outside++;
                if (note.beat == 0f)
                {
                    strong++;
                    bool inChord = false;
                    foreach (int m in SoundGen.MenuChordAt(note.bar).pad) if ((m - note.midi) % 12 == 0) inChord = true;
                    if (!inChord) { strongOutside++; UnityEngine.Debug.Log("MENU MUSIC INFO bar " + (note.bar + 1) + ": strong-beat note " + note.midi + " is not in " + SoundGen.MenuChordAt(note.bar).name); }
                }
            }
            bool threeBeats = true;
            foreach (float l in lengths) if (Mathf.Abs(l - 3f) > 0.001f) threeBeats = false;
            Check("every bar of the melody lasts three beats", threeBeats);
            Check("the melody has " + notes.Count + " notes (at least 60)", notes.Count >= 60);
            Check("every melody note lies between D4 and E6 (" + outside + " outside)", outside == 0);
            Check("strong-beat notes outside their chord: " + strongOutside + " of " + strong + " (at most 4)", strongOutside <= 4);
            Check("the last note is the leading tone C#5 (bar 32)", notes[notes.Count - 1].midi == SoundGen.MidiOf("C#5") && notes[notes.Count - 1].bar == SoundGen.MenuBars - 1);
            string[] names = { "Dm", "Bb", "Gm", "A7", "F", "C" };
            for (int bar = 0; bar < SoundGen.MenuBars; bar++)
            {
                string name = SoundGen.MenuChordAt(bar).name;
                if (Array.IndexOf(names, name) < 0) { Check("chord " + name + " is known", false); break; }
            }
        }

        /// <summary>La clip che il gioco usa: stessi campioni in una clip stereo a 22,05 kHz, pronta dopo il thread.</summary>
        static void CheckClip(int frames)
        {
            SoundBank.WarmMenuMusic();
            var wait = Stopwatch.StartNew();
            while (!SoundBank.MenuThemeReady && wait.Elapsed.TotalSeconds < 30.0) System.Threading.Thread.Sleep(50);
            Check("the menu theme thread finishes (" + wait.Elapsed.TotalSeconds.ToString("0.0") + " s)", SoundBank.MenuThemeReady);
            var clip = SoundBank.MenuTheme;
            Check("the clip is stereo, 22,05 kHz, 96 s", clip != null && clip.channels == 2 && clip.frequency == SoundGen.MenuRate && clip.samples == frames);
        }

        static void WriteWav(string path, float[] samples, int channels, int rate)
        {
            using (var stream = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(stream))
            {
                int bytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
                w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                foreach (float s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
            }
        }
    }
}
